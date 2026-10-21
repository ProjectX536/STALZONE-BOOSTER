namespace Booster.Platform.Windows.Common;

/// <summary>
/// Одно окно Booster в сеансе пользователя: второе окно при старте восстановило бы сессию, которую ведёт первое.
/// Держится открытый дескриптор именованного мьютекса без владения — он не привязан к потоку и закрывается вместе с процессом.
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    public const string DefaultName = @"Local\Booster.SingleInstance";

    private readonly Mutex _mutex;

    private SingleInstanceLock(Mutex mutex) => _mutex = mutex;

    /// <summary>null — объект уже существует: Booster запущен (в том числе с другими правами — тогда доступ запрещён).</summary>
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
