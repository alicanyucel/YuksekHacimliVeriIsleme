using YuksekHacimliVeriIsleme.Domain.Entities;

namespace YuksekHacimliVeriIsleme.Application.Features.CustomerImports;

public interface ICustomerImportService
{
    Task<CustomerImportJobDto> UploadAsync(Stream content, string fileName, long fileSize, CancellationToken cancellationToken);
}

public sealed record CustomerImportCandidate(Customer Customer, long RowNumber, string RawData);

public interface ICustomerImportRepository
{
    Task AddJobAsync(CustomerImportJob job, CancellationToken cancellationToken);
    Task SaveBatchAsync(
        CustomerImportJob job,
        IReadOnlyCollection<CustomerImportCandidate> candidates,
        IReadOnlyCollection<CustomerImportError> validationErrors,
        long processedCount,
        CancellationToken cancellationToken);
    Task<CustomerImportPage<CustomerImportJobDto>> GetJobsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<CustomerImportJobDto?> GetJobAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerImportPage<CustomerImportErrorDto>?> GetErrorsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken);
    IAsyncEnumerable<CustomerImportErrorDto> StreamErrorsAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICustomerImportQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}

public interface ICustomerImportProcessor
{
    Task RecoverPendingJobsAsync(CancellationToken cancellationToken);
    Task ProcessAsync(Guid jobId, CancellationToken cancellationToken);
}
