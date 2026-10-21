using System.Text.Json;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.TestSupport;

public static class WmiFixture
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wmi");

    public static IReadOnlyList<CimRow> Load(string relativePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, relativePath)));
        var root = doc.RootElement;
        var objects = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root];
        return objects.Select(ToRow).ToArray();
    }

    public static CimRow LoadSingle(string relativePath) => Assert.Single(Load(relativePath));

    public static IReadOnlyDictionary<string, IReadOnlyList<CimRow>> LoadFolder(string folder) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), "*.json")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => Load(Path.Combine(folder, Path.GetFileName(f))),
                StringComparer.OrdinalIgnoreCase);

    public static CimRow Row(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(p => p.Name, p => p.Value));

    private static CimRow ToRow(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Fixture row must be an object, got {element.ValueKind}");
        return new CimRow(element.EnumerateObject().ToDictionary(p => p.Name, p => ToValue(p.Value)));
    }

    private static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray().Select(ToValue).ToArray(),
        _ => throw new InvalidDataException($"Unsupported fixture value kind {value.ValueKind}"),
    };
}
