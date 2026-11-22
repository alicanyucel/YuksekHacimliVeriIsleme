using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;
using YuksekHacimliVeriIsleme.Domain.Entities;
using YuksekHacimliVeriIsleme.Infrastructure.Options;

namespace YuksekHacimliVeriIsleme.Infrastructure.Services;

internal sealed class CustomerImportService : ICustomerImportService
{
    private readonly ICustomerImportRepository _repository;
    private readonly ICustomerImportQueue _queue;
    private readonly CustomerImportOptions _options;
    private readonly IHostEnvironment _environment;

    public CustomerImportService(
        ICustomerImportRepository repository,
        ICustomerImportQueue queue,
        IOptions<CustomerImportOptions> options,
        IHostEnvironment environment)
    {
        _repository = repository;
        _queue = queue;
        _options = options.Value;
        _environment = environment;
    }

    public async Task<CustomerImportJobDto> UploadAsync(
        Stream content,
        string fileName,
        long fileSize,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Yalnızca .csv uzantılı dosyalar yüklenebilir.");
        }

        if (fileSize <= 0 || fileSize > _options.MaxFileSizeBytes)
        {
            throw new InvalidDataException($"Dosya boş olamaz ve {_options.MaxFileSizeBytes} bayttan büyük olamaz.");
        }

        var id = Guid.NewGuid();
        var trackingNumber = $"CI-{DateTime.UtcNow:yyyyMMdd}-{id:N}";
        var uploadDirectory = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.UploadDirectory));
        Directory.CreateDirectory(uploadDirectory);
        var filePath = Path.Combine(uploadDirectory, $"{id:N}.csv");
        var jobPersisted = false;

        try
        {
            await using (var destination = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long copiedBytes = 0;
                int bytesRead;
                while ((bytesRead = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    copiedBytes += bytesRead;
                    if (copiedBytes > _options.MaxFileSizeBytes)
                    {
                        throw new InvalidDataException($"Dosya {_options.MaxFileSizeBytes} bayt sınırını aşıyor.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                if (copiedBytes != fileSize)
                {
                    throw new InvalidDataException("Yüklenen dosya boyutu bildirilen değerle eşleşmiyor.");
                }
            }

            var job = new CustomerImportJob
            {
                Id = id,
                TrackingNumber = trackingNumber,
                FileName = Path.GetFileName(fileName),
                FilePath = filePath,
                UploadedAt = DateTime.UtcNow,
                Status = CustomerImportStatus.Pending
            };

            await _repository.AddJobAsync(job, cancellationToken);
            jobPersisted = true;
            await _queue.EnqueueAsync(job.Id);
            return ToDto(job);
        }
        catch
        {
            if (!jobPersisted && File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            throw;
        }
    }

    private static CustomerImportJobDto ToDto(CustomerImportJob job) => new(
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
        job.FailureReason);
}
