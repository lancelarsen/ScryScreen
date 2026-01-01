using System.Threading;
using System.Threading.Tasks;

namespace ScryScreen.App.Services;

public sealed class TaskVideoDelay : IVideoDelay
{
    public Task Delay(int milliseconds, CancellationToken cancellationToken = default)
        => Task.Delay(milliseconds, cancellationToken);
}
