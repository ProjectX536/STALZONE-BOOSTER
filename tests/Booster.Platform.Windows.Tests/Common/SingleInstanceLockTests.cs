using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public class SingleInstanceLockTests
{
    // Своё имя на каждый тест, чтобы не мешал запущенный Booster.
    private readonly string _name = @"Local\Booster.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Second_acquire_fails_while_first_is_held()
    {
        using var first = SingleInstanceLock.TryAcquire(_name);

        Assert.NotNull(first);
        Assert.Null(SingleInstanceLock.TryAcquire(_name));
    }

    [Fact]
    public void Lock_is_free_again_after_dispose()
    {
        SingleInstanceLock.TryAcquire(_name)!.Dispose();

        using var second = SingleInstanceLock.TryAcquire(_name);

        Assert.NotNull(second);
    }

    [Fact]
    public async Task Lock_held_by_another_thread_blocks_acquire()
    {
        var first = await Task.Run(() => SingleInstanceLock.TryAcquire(_name), TestContext.Current.CancellationToken);
        try
        {
            Assert.Null(SingleInstanceLock.TryAcquire(_name));
        }
        finally
        {
            first?.Dispose();
        }
    }
}
