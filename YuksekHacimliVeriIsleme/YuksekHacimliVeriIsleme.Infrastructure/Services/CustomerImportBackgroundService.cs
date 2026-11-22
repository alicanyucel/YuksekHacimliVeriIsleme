using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;
using YuksekHacimliVeriIsleme.Infrastructure.Options;

namespace YuksekHacimliVeriIsleme.Infrastructure.Services;

internal sealed class CustomerImportBackgroundService : BackgroundService
{
    private readonly ICustomerImportQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CustomerImportOptions _options;
    private readonly ILogger<CustomerImportBackgroundService> _logger;

    public CustomerImportBackgroundService(
        ICustomerImportQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<CustomerImportOptions> options,
        ILogger<CustomerImportBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICustomerImportProcessor>();
            await processor.RecoverPendingJobsAsync(stoppingToken);
        }

        var workers = Enumerable.Range(0, Math.Max(1, _options.MaxConcurrentJobs))
            .Select(_ => ConsumeAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var jobId in _queue.ReadAllAsync(cancellationToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ICustomerImportProcessor>();
                await processor.ProcessAsync(jobId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Customer import job {JobId} could not be processed.", jobId);
            }
        }
    }
}
