using System.Threading;
using System.Threading.Tasks;

namespace ScryScreen.App.Services;

public interface IVideoDelay
{
    Task Delay(int milliseconds, CancellationToken cancellationToken = default);
}
