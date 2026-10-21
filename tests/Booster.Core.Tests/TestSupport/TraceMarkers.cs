namespace Booster.Core.Tests.TestSupport;

/// <summary>Маркер записи WPR в формате <c>TraceRecorder</c>: схема 1 и время начала.</summary>
public static class TraceMarkers
{
    public static string Json(DateTimeOffset startedAt) => $$"""{ "schemaVersion": 1, "startedAt": "{{startedAt:O}}" }""";
}
