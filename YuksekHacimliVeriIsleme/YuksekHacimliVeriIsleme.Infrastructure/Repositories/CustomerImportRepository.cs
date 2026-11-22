using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;
using YuksekHacimliVeriIsleme.Domain.Entities;
using YuksekHacimliVeriIsleme.Infrastructure.Context;

namespace YuksekHacimliVeriIsleme.Infrastructure.Repositories;

internal sealed class CustomerImportRepository : ICustomerImportRepository
{
    private const int MaximumPageSize = 500;
    private readonly ApplicationDbContext _dbContext;

    public CustomerImportRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddJobAsync(CustomerImportJob job, CancellationToken cancellationToken)
    {
        _dbContext.CustomerImportJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveBatchAsync(
        CustomerImportJob job,
        IReadOnlyCollection<CustomerImportCandidate> candidates,
        IReadOnlyCollection<CustomerImportError> validationErrors,
        long processedCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var customers = candidates.Select(candidate => candidate.Customer).ToArray();
        var customerIds = customers.Select(customer => customer.CustomerId).Distinct().ToArray();
        var nationalIds = customers.Select(customer => customer.NationalId).Distinct().ToArray();
        var existingCustomerIds = customerIds.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _dbContext.Customers.AsNoTracking().Where(customer => customerIds.Contains(customer.CustomerId))
                .Select(customer => customer.CustomerId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var existingNationalIds = nationalIds.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _dbContext.Customers.AsNoTracking().Where(customer => nationalIds.Contains(customer.NationalId))
                .Select(customer => customer.NationalId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var seenCustomerIds = new HashSet<string>(StringComparer.Ordinal);
        var seenNationalIds = new HashSet<string>(StringComparer.Ordinal);
        var customersToInsert = new List<Customer>(customers.Length);
        var duplicateErrors = new List<CustomerImportError>();
        foreach (var candidate in candidates)
        {
            var customer = candidate.Customer;
            if (existingCustomerIds.Contains(customer.CustomerId) || existingNationalIds.Contains(customer.NationalId)
                || !seenCustomerIds.Add(customer.CustomerId) || !seenNationalIds.Add(customer.NationalId))
            {
                duplicateErrors.Add(new CustomerImportError
                {
                    CustomerImportJobId = job.Id,
                    RowNumber = candidate.RowNumber,
                    RawData = candidate.RawData.Length > 4000 ? candidate.RawData[..4000] : candidate.RawData,
                    ErrorReason = "Customer ID veya T.C. kimlik numarası daha önce kayıtlı."
                });
            }
            else
            {
                customersToInsert.Add(customer);
            }
        }

        var oldProcessedRows = job.ProcessedRows;
        var oldSuccessCount = job.SuccessCount;
        var oldErrorCount = job.ErrorCount;
        var oldDuplicateCount = job.DuplicateCount;
        var errors = validationErrors.Concat(duplicateErrors).ToArray();
        try
        {
            if (customersToInsert.Count > 0)
            {
                await BulkInsertCustomersAsync(customersToInsert, transaction.GetDbTransaction(), cancellationToken);
            }

            if (errors.Length > 0)
            {
                _dbContext.CustomerImportErrors.AddRange(errors);
            }

            job.ProcessedRows += processedCount;
            job.SuccessCount += customersToInsert.Count;
            job.ErrorCount += validationErrors.Count;
            job.DuplicateCount += duplicateErrors.Count;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            job.ProcessedRows = oldProcessedRows;
            job.SuccessCount = oldSuccessCount;
            job.ErrorCount = oldErrorCount;
            job.DuplicateCount = oldDuplicateCount;
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            foreach (var error in errors)
            {
                _dbContext.Entry(error).State = EntityState.Detached;
            }
        }
    }

    private async Task BulkInsertCustomersAsync(
        IReadOnlyCollection<Customer> customers,
        DbTransaction dbTransaction,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("CustomerId", typeof(string));
        table.Columns.Add("FirstName", typeof(string));
        table.Columns.Add("LastName", typeof(string));
        table.Columns.Add("NationalId", typeof(string));
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("Phone", typeof(string));
        table.Columns.Add("BirthDate", typeof(DateTime));
        table.Columns.Add("CreatedDate", typeof(DateTime));
        foreach (var customer in customers)
        {
            table.Rows.Add(customer.Id, customer.CustomerId, customer.FirstName, customer.LastName,
                customer.NationalId, customer.Email, customer.Phone,
                customer.BirthDate.ToDateTime(TimeOnly.MinValue), customer.CreatedDate);
        }

        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        var transaction = (SqlTransaction)dbTransaction;
        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = "[dbo].[Customers]",
            BatchSize = customers.Count,
            BulkCopyTimeout = 0
        };
        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(table, cancellationToken);
    }

    public async Task<CustomerImportPage<CustomerImportJobDto>> GetJobsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var query = _dbContext.CustomerImportJobs.AsNoTracking();
        var totalCount = await query.LongCountAsync(cancellationToken);
        var jobs = await query
            .OrderByDescending(job => job.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(job => new CustomerImportJobDto(
                job.Id,
                job.TrackingNumber,
                job.FileName,
                job.Status,
                job.TotalRows,
                job.ProcessedRows,
                job.SuccessCount,
                job.ErrorCount,
                job.DuplicateCount,
                job.UploadedAt,
                job.StartedAt,
                job.CompletedAt,
                job.FailureReason))
            .ToListAsync(cancellationToken);

        return new CustomerImportPage<CustomerImportJobDto>(jobs, page, pageSize, totalCount);
    }

    public Task<CustomerImportJobDto?> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.CustomerImportJobs.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new CustomerImportJobDto(
                item.Id,
                item.TrackingNumber,
                item.FileName,
                item.Status,
                item.TotalRows,
                item.ProcessedRows,
                item.SuccessCount,
                item.ErrorCount,
                item.DuplicateCount,
                item.UploadedAt,
                item.StartedAt,
                item.CompletedAt,
                item.FailureReason))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CustomerImportPage<CustomerImportErrorDto>?> GetErrorsAsync(
        Guid id,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await _dbContext.CustomerImportJobs.AsNoTracking().AnyAsync(job => job.Id == id, cancellationToken))
        {
            return null;
        }

        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var query = _dbContext.CustomerImportErrors.AsNoTracking().Where(error => error.CustomerImportJobId == id);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var errors = await query
            .OrderBy(error => error.RowNumber)
            .ThenBy(error => error.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(error => new CustomerImportErrorDto(error.RowNumber, error.RawData, error.ErrorReason))
            .ToListAsync(cancellationToken);

        return new CustomerImportPage<CustomerImportErrorDto>(errors, page, pageSize, totalCount);
    }

    public async IAsyncEnumerable<CustomerImportErrorDto> StreamErrorsAsync(
        Guid id,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var errors = _dbContext.CustomerImportErrors.AsNoTracking()
            .Where(error => error.CustomerImportJobId == id)
            .OrderBy(error => error.RowNumber)
            .ThenBy(error => error.Id)
            .AsAsyncEnumerable();

        await foreach (var error in errors.WithCancellation(cancellationToken))
        {
            yield return new CustomerImportErrorDto(error.RowNumber, error.RawData, error.ErrorReason);
        }
    }
}
