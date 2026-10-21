using System.Text.Json;

namespace Booster.Core.Configuration;

/// <summary>Общие параметры разбора и записи файлов конфигурации.</summary>
public static class ConfigJson
{
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// camelCase, комментарии и висячие запятые допускаются. Отсутствующее обязательное поле
    /// или null в ненулевом поле — ошибка разбора, а не тихое значение по умолчанию.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
    };
}
