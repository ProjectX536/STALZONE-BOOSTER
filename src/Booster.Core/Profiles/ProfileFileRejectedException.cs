namespace Booster.Core.Profiles;

/// <summary>
/// Пользовательский файл профиля при загрузке не применён (повреждён, записан более новой версией, не прочитан).
/// Сохранение его не перезаписывает: в файле могут быть настройки, которые пользователь потерял бы.
/// </summary>
public sealed class ProfileFileRejectedException : InvalidOperationException
{
    public ProfileFileRejectedException()
    {
    }

    public ProfileFileRejectedException(string message) : base(message)
    {
    }

    public ProfileFileRejectedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ProfileFileRejectedException(string fileName, string detail)
        : base($"{fileName}: user profile file was not applied ({detail}); it is not overwritten")
    {
        FileName = fileName;
    }

    public string? FileName { get; }
}
