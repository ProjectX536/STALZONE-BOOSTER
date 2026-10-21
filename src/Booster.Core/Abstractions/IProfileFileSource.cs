namespace Booster.Core.Abstractions;

/// <summary>Файлы профилей: встроенные — рядом с exe (<c>config\Profiles</c>), пользовательские — в профиле пользователя.</summary>
public interface IProfileFileSource
{
    /// <summary>Имена <c>*.json</c> встроенных профилей, по возрастанию.</summary>
    IReadOnlyList<string> ListBuiltIn();

    string ReadBuiltIn(string fileName);

    /// <summary>null — файла нет. Оставшийся от прерванной записи .tmp удаляется.</summary>
    string? ReadUser(string fileName);

    /// <summary>Атомарная запись; каталог создаётся при необходимости.</summary>
    Task WriteUserAsync(string fileName, string json, CancellationToken ct);
}
