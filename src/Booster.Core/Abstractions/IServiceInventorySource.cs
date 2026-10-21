namespace Booster.Core.Abstractions;

public enum ServiceStartMode
{
    Boot,
    System,
    Automatic,
    Manual,
    Disabled,
    Unknown,
}

public enum ServiceRunState
{
    Running,
    Stopped,
    Paused,

    /// <summary>Запускается, останавливается, приостанавливается или продолжается.</summary>
    Pending,
    Unknown,
}

/// <summary>Служба Windows.</summary>
/// <param name="ImagePath">Исполняемый файл без аргументов; у служб svchost — ServiceDll.</param>
/// <param name="ProcessId">Null, если служба не запущена.</param>
/// <param name="Publisher">CompanyName исполняемого файла (у служб svchost — ServiceDll).</param>
/// <param name="DependentServices">Службы, которые зависят от этой.</param>
public sealed record ServiceInfo(string Name, string DisplayName, ServiceRunState State, ServiceStartMode StartMode, bool DelayedAutoStart,
    string? ImagePath, int? ProcessId, Reading<string> Publisher, IReadOnlyList<string> DependentServices);

/// <summary>Список служб. Только чтение: Booster службы не останавливает и не перенастраивает.</summary>
public interface IServiceInventorySource
{
    Task<IReadOnlyList<ServiceInfo>> ReadAsync(CancellationToken ct);
}
