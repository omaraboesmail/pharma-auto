using System.Threading.Channels;
using PharmaAuto.Connector.Application;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class InvoiceWorkflowQueue : IInvoiceWorkflowQueue
{
    private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
        channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
