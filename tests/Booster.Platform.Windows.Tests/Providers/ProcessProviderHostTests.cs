using System.Diagnostics;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers;

/// <summary>
/// Читают список процессов, время старта и приоритет; процессы не меняются, SetPriority не вызывается. Запускается только
/// безвредный cmd /c exit 0 — чтобы получить завершённый процесс.
/// </summary>
[Trait("Category", "Host")]
public class ProcessProviderHostTests
{
    private readonly ProcessProvider _provider = new();

    [Fact]
    public void List_contains_own_process()
    {
        using var own = Process.GetCurrentProcess();

        Assert.Contains(new ProcessInfo(own.Id, own.ProcessName), _provider.List());
    }

    [Fact]
    public void Identify_gives_own_start_time()
    {
        using var own = Process.GetCurrentProcess();

        var identity = _provider.Identify(own.Id);

        Assert.NotNull(identity);
        Assert.Equal(own.Id, identity.Value.ProcessId);
        Assert.Equal(own.StartTime.ToUniversalTime(), identity.Value.StartTime.UtcDateTime);
    }

    [Fact]
    public void Own_priority_matches_the_process_class()
    {
        using var own = Process.GetCurrentProcess();
        var identity = _provider.Identify(own.Id)!.Value;

        Assert.Equal(own.PriorityClass.ToString(), _provider.GetPriority(identity).ToString());
    }

    [Fact]
    public void Priority_of_a_process_with_another_start_time_is_null()
    {
        using var own = Process.GetCurrentProcess();
        var identity = _provider.Identify(own.Id)!.Value;

        Assert.Null(_provider.GetPriority(identity with { StartTime = identity.StartTime.AddSeconds(-1) }));
        Assert.False(_provider.CanSetPriority(identity with { StartTime = identity.StartTime.AddSeconds(-1) }));
    }

    [Fact]
    public void Own_process_can_have_its_priority_set()
    {
        using var own = Process.GetCurrentProcess();

        // Только открытие дескриптора с PROCESS_SET_INFORMATION; SetPriority не вызывается.
        Assert.True(_provider.CanSetPriority(_provider.Identify(own.Id)!.Value));
    }

    [Fact]
    public void Exited_process_held_open_is_not_identified()
    {
        // Единственный запуск в тестах — cmd, который сразу выходит. Объект Process держит его дескриптор, как лаунчер
        // держит дескриптор игры: процесс завершён, но OpenProcess по его PID ещё удаётся.
        using var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        child.WaitForExit();
        var identity = new ProcessIdentity(child.Id, child.StartTime);

        Assert.Null(_provider.Identify(child.Id));
        Assert.Null(_provider.GetPriority(identity));
        Assert.False(_provider.CanSetPriority(identity));
    }

    [Fact]
    public void Identify_of_missing_process_is_null()
    {
        var running = _provider.List().Select(p => p.ProcessId).ToHashSet();
        var missing = Enumerable.Range(0, 1000).Select(i => 0x3FFF_0000 + i * 4).First(id => !running.Contains(id));

        Assert.Null(_provider.Identify(missing));
    }
}
