using System.Threading.Channels;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
namespace RoboTransfer.App.Services;

// At most one queued progress snapshot; filesystem work never runs inside an async-void callback.
public sealed class ReceiverProgressPublisher : IAsyncDisposable
{
    private readonly Channel<(long Files, long Bytes)> pending = Channel.CreateBounded<(long, long)>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task writer;
    public ReceiverProgressPublisher(ReceiverInboxService? service, PolicyProfile policy, MigrationExecutionPlan plan, ReceiverPhase phase)
    {
        writer = Task.Run(RunAsync);
        async Task RunAsync()
        {
            await foreach (var progress in pending.Reader.ReadAllAsync())
            {
                if (service is not null && plan.Receiver is not null)
                {
                    await service.PublishAsync(policy, plan.Receiver, plan.SessionId, phase, progress.Files, progress.Bytes);
                    // Limit status traffic independently of the number of tiny files being copied.
                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                }
            }
        }
    }
    public void Report(long files, long bytes) => pending.Writer.TryWrite((files, bytes));
    public async ValueTask DisposeAsync() { pending.Writer.TryComplete(); await writer; }
}
