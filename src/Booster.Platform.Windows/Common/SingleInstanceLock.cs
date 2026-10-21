namespace Booster.Platform.Windows.Common;

public sealed class SingleInstanceLock : IDisposable
{
    public const string DefaultName = @"Local\Booster.SingleInstance";

    private readonly Mutex _mutex;

    private SingleInstanceLock(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(false, name, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (createdNew)
            return new SingleInstanceLock(mutex);

        mutex.Dispose();
        return null;
    }

    public void Dispose() => _mutex.Dispose();
}
