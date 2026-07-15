namespace CodexRemaining.App.Infrastructure;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\CodexRemaining.SingleInstance";
    private const string EventName = "Local\\CodexRemaining.Activate";

    private readonly Mutex mutex = new(initiallyOwned: false, MutexName);
    private readonly EventWaitHandle activationEvent = new(
        initialState: false,
        EventResetMode.AutoReset,
        EventName);
    private RegisteredWaitHandle? registeredWait;
    private bool ownsMutex;

    public bool TryAcquire()
    {
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return ownsMutex;
    }

    public void SignalExistingInstance() => activationEvent.Set();

    public void StartListening(Action callback)
    {
        registeredWait = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    callback();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        registeredWait?.Unregister(null);
        activationEvent.Dispose();

        if (ownsMutex)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}
