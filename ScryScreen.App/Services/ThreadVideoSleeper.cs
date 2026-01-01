using System.Threading;

namespace ScryScreen.App.Services;

public sealed class ThreadVideoSleeper : IVideoSleeper
{
    public void Sleep(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        Thread.Sleep(milliseconds);
    }
}
