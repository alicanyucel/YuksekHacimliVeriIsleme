using System.Threading.Channels;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;

namespace YuksekHacimliVeriIsleme.Infrastructure.Services;

internal sealed class CustomerImportQueue : ICustomerImportQueue
{
    private readonly Channel<Guid> _jobs = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        AllowSynchronousContinuations = false,
        SingleReader = false,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _jobs.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _jobs.Reader.ReadAllAsync(cancellationToken);
}
