using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;
using YuksekHacimliVeriIsleme.Domain.Entities;
using YuksekHacimliVeriIsleme.Infrastructure.Context;
using YuksekHacimliVeriIsleme.Infrastructure.Options;

namespace YuksekHacimliVeriIsleme.Infrastructure.Services;

internal sealed class CustomerImportProcessor : ICustomerImportProcessor
{
    private static readonly string[] RequiredHeaders =
    [
        "Customer ID", "First Name", "Last Name", "National ID", "Email", "Phone", "Birth Date", "Created Date"
    ];

    private static readonly string[] BirthDateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"];
    private static readonly string[] CreatedDateFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss"];

    private readonly ApplicationDbContext _dbContext;
    private readonly ICustomerImportRepository _repository;
    private readonly ICustomerImportQueue _queue;
    private readonly CustomerImportOptions _options;
    private readonly ILogger<CustomerImportProcessor> _logger;

    public CustomerImportProcessor(
        ApplicationDbContext dbContext,
        ICustomerImportRepository repository,
        ICustomerImportQueue queue,
        IOptions<CustomerImportOptions> options,
        ILogger<CustomerImportProcessor> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RecoverPendingJobsAsync(CancellationToken cancellationToken)
    {
        var pendingJobs = await _dbContext.CustomerImportJobs.AsNoTracking()
            .Where(job => job.Status == CustomerImportStatus.Pending || job.Status == CustomerImportStatus.Processing)
            .OrderBy(job => job.UploadedAt)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);

        foreach (var jobId in pendingJobs)
        {
            await _queue.EnqueueAsync(jobId, cancellationToken);
        }
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.CustomerImportJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null || job.Status is CustomerImportStatus.Completed or CustomerImportStatus.Failed or CustomerImportStatus.PartiallyCompleted)
        {
            return;
        }

        job.Status = CustomerImportStatus.Processing;
        job.StartedAt ??= DateTime.UtcNow;
        job.FailureReason = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await ProcessFileAsync(job, cancellationToken);
            job.CompletedAt = DateTime.UtcNow;
            job.Status = job.ErrorCount == 0 && job.DuplicateCount == 0
                ? CustomerImportStatus.Completed
                : job.SuccessCount > 0
                    ? CustomerImportStatus.PartiallyCompleted
                    : CustomerImportStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (File.Exists(job.FilePath))
            {
                File.Delete(job.FilePath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Customer import job {JobId} failed.", jobId);
            job.CompletedAt = DateTime.UtcNow;
            job.FailureReason = exception is InvalidDataException
                ? exception.Message
                : "Dosya işlenirken beklenmeyen bir hata oluştu. Ayrıntılar sunucu günlüklerine yazıldı.";
            job.Status = job.SuccessCount > 0
                ? CustomerImportStatus.PartiallyCompleted
                : CustomerImportStatus.Failed;
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            if (File.Exists(job.FilePath))
            {
                File.Delete(job.FilePath);
            }
        }
    }

    private async Task ProcessFileAsync(CustomerImportJob job, CancellationToken cancellationToken)
    {
        if (!File.Exists(job.FilePath))
        {
            throw new InvalidDataException("Yüklenen dosya bulunamadı.");
        }

        job.TotalRows = CountDataRows(job.FilePath, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        using var parser = new TextFieldParser(job.FilePath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        if (parser.EndOfData)
        {
            throw new InvalidDataException("CSV dosyası boş.");
        }

        var headers = parser.ReadFields() ?? [];
        if (headers.Length > 0)
        {
            headers[0] = headers[0].TrimStart('\uFEFF');
        }

        var headerIndexes = headers
            .Select((header, index) => new { Name = header.Trim(), Index = index })
            .GroupBy(header => header.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var missingHeaders = RequiredHeaders.Where(header => !headerIndexes.ContainsKey(header)).ToArray();
        if (missingHeaders.Length > 0)
        {
            throw new InvalidDataException($"CSV başlıkları eksik: {string.Join(", ", missingHeaders)}.");
        }

        long skippedRows = 0;
        while (skippedRows < job.ProcessedRows && !parser.EndOfData)
        {
            parser.ReadFields();
            skippedRows++;
        }

        if (skippedRows != job.ProcessedRows)
        {
            throw new InvalidDataException("İşlem kaldığı yerden sürdürülemedi; dosyadaki satır sayısı değişmiş olabilir.");
        }

        var batch = new List<ImportRow>(_options.BatchSize);
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowNumber = parser.LineNumber;
            var fields = parser.ReadFields() ?? [];
            var errors = new List<string>();
            Customer? customer = null;

            if (fields.Length != headers.Length)
            {
                errors.Add($"Sütun sayısı hatalı. Beklenen: {headers.Length}, bulunan: {fields.Length}.");
            }
            else
            {
                customer = ValidateRow(fields, headerIndexes, errors);
            }

            batch.Add(new ImportRow(rowNumber, fields, customer, errors));
            if (batch.Count >= _options.BatchSize)
            {
                await SaveBatchAsync(job, batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await SaveBatchAsync(job, batch, cancellationToken);
        }

        if (job.ProcessedRows != job.TotalRows)
        {
            throw new InvalidDataException("Dosya işlenirken satır sayısı değişti; işlem tamamlanamadı.");
        }
    }

    private static long CountDataRows(string filePath, CancellationToken cancellationToken)
    {
        using var parser = new TextFieldParser(filePath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        if (!parser.EndOfData)
        {
            parser.ReadFields();
        }

        long rowCount = 0;
        while (!parser.EndOfData)
        {
            if (rowCount % 1000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            parser.ReadFields();
            rowCount++;
        }

        return rowCount;
    }

    private async Task SaveBatchAsync(CustomerImportJob job, List<ImportRow> batch, CancellationToken cancellationToken)
    {
        var candidates = new List<CustomerImportCandidate>(batch.Count);
        var validationErrors = new List<CustomerImportError>();
        foreach (var row in batch)
        {
            if (row.Customer is null)
            {
                validationErrors.Add(ToError(job.Id, row, string.Join(" ", row.Errors)));
                continue;
            }

            candidates.Add(new CustomerImportCandidate(row.Customer, row.RowNumber, SerializeRawData(row.Fields)));
        }

        await _repository.SaveBatchAsync(job, candidates, validationErrors, batch.Count, cancellationToken);
    }

    private static Customer? ValidateRow(string[] fields, IReadOnlyDictionary<string, int> indexes, List<string> errors)
    {
        var customerId = GetField(fields, indexes, "Customer ID");
        var firstName = GetField(fields, indexes, "First Name");
        var lastName = GetField(fields, indexes, "Last Name");
        var nationalId = GetField(fields, indexes, "National ID");
        var email = GetField(fields, indexes, "Email");
        var phone = GetField(fields, indexes, "Phone");
        var birthDateValue = GetField(fields, indexes, "Birth Date");
        var createdDateValue = GetField(fields, indexes, "Created Date");

        AddRequiredError(customerId, "Customer ID", errors);
        AddRequiredError(firstName, "First Name", errors);
        AddRequiredError(lastName, "Last Name", errors);
        AddRequiredError(nationalId, "National ID", errors);
        AddRequiredError(email, "Email", errors);
        AddRequiredError(phone, "Phone", errors);
        AddRequiredError(birthDateValue, "Birth Date", errors);
        AddRequiredError(createdDateValue, "Created Date", errors);

        if (customerId.Length > 100) errors.Add("Customer ID en fazla 100 karakter olabilir.");
        if (firstName.Length > 100) errors.Add("First Name en fazla 100 karakter olabilir.");
        if (lastName.Length > 100) errors.Add("Last Name en fazla 100 karakter olabilir.");
        if (email.Length > 254) errors.Add("Email en fazla 254 karakter olabilir.");
        if (phone.Length > 30) errors.Add("Phone en fazla 30 karakter olabilir.");
        if (nationalId.Length > 11 || !IsValidNationalId(nationalId)) errors.Add("T.C. kimlik numarası geçersiz.");
        if (!IsValidEmail(email)) errors.Add("E-posta adresi geçersiz.");
        if (!IsValidPhone(phone)) errors.Add("Telefon numarası geçersiz.");

        var hasBirthDate = DateOnly.TryParseExact(
            birthDateValue,
            BirthDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var birthDate);
        if (!hasBirthDate)
        {
            errors.Add("Birth Date biçimi geçersiz; yyyy-MM-dd veya dd.MM.yyyy kullanın.");
        }
        else if (birthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors.Add("Birth Date gelecekte olamaz.");
        }

        var hasCreatedDate = DateTime.TryParseExact(
            createdDateValue,
            CreatedDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var createdDate);
        if (!hasCreatedDate)
        {
            errors.Add("Created Date biçimi geçersiz; yyyy-MM-dd veya yyyy-MM-dd HH:mm:ss kullanın.");
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new Customer
        {
            CustomerId = customerId,
            FirstName = firstName,
            LastName = lastName,
            NationalId = nationalId,
            Email = email,
            Phone = phone,
            BirthDate = birthDate,
            CreatedDate = createdDate
        };
    }

    private static string GetField(string[] fields, IReadOnlyDictionary<string, int> indexes, string header) =>
        fields[indexes[header]].Trim();

    private static void AddRequiredError(string value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} zorunludur.");
        }
    }

    private static bool IsValidNationalId(string value)
    {
        if (value.Length != 11 || value[0] == '0' || value.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        var digits = value.Select(character => character - '0').ToArray();
        var oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var evenSum = digits[1] + digits[3] + digits[5] + digits[7];
        var tenthDigit = (oddSum * 7 - evenSum) % 10;
        if (tenthDigit < 0) tenthDigit += 10;
        return digits[9] == tenthDigit && digits[10] == digits.Take(10).Sum() % 10;
    }

    private static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)) return false;

        try
        {
            var atIndex = value.IndexOf('@');
            return atIndex > 0
                && new MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase)
                && value.IndexOf('.', atIndex + 1) > atIndex + 1;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidPhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var digits = value.Count(character => character is >= '0' and <= '9');
        return digits is >= 7 and <= 15
            && value.All(character => char.IsDigit(character) || character is '+' or '-' or '(' or ')' or ' ' or '.');
    }

    private static CustomerImportError ToError(Guid jobId, ImportRow row, string reason) => new()
    {
        CustomerImportJobId = jobId,
        RowNumber = row.RowNumber,
        RawData = SerializeRawData(row.Fields),
        ErrorReason = reason.Length > 2000 ? reason[..2000] : reason
    };

    private static string SerializeRawData(string[] fields)
    {
        var rawData = JsonSerializer.Serialize(fields);
        return rawData.Length > 4000 ? rawData[..4000] : rawData;
    }

    private readonly record struct ImportRow(long RowNumber, string[] Fields, Customer? Customer, List<string> Errors);
}
