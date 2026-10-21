namespace Booster.Core.Tests.TestSupport;

public static class TraceMarkers
{
    public static string Json(DateTimeOffset startedAt) => $$"""{ "schemaVersion": 1, "startedAt": "{{startedAt:O}}" }""";
}
