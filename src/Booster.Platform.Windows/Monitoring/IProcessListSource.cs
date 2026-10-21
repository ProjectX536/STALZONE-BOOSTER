namespace Booster.Platform.Windows.Monitoring;

/// <summary>Процесс с видимым главным окном. <see cref="Name"/> — имя exe без расширения.</summary>
public sealed record ProcessEntry(int ProcessId, string Name, string WindowTitle);

/// <summary>Список процессов для выбора цели замера; только чтение, подменяется в тестах.</summary>
public interface IProcessListSource
{
    /// <summary>Процессы с видимым окном и непустым заголовком, кроме самого Booster; по имени, затем по PID.</summary>
    IReadOnlyList<ProcessEntry> GetWindowedProcesses();
}
