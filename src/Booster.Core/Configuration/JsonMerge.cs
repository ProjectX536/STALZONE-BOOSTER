using System.Text.Json.Nodes;

namespace Booster.Core.Configuration;

public static class JsonMerge
{
    public static JsonObject Merge(JsonObject baseDoc, JsonObject overlay)
    {
        ArgumentNullException.ThrowIfNull(baseDoc);
        ArgumentNullException.ThrowIfNull(overlay);

        var result = baseDoc.DeepClone().AsObject();
        MergeInto(result, overlay);
        return result;
    }

    public static JsonObject Diff(JsonObject baseDoc, JsonObject target)
    {
        ArgumentNullException.ThrowIfNull(baseDoc);
        ArgumentNullException.ThrowIfNull(target);

        var diff = new JsonObject();
        foreach (var (name, value) in target)
        {
            var hasBase = baseDoc.TryGetPropertyValue(name, out var baseValue);
            if (hasBase && value is JsonObject targetChild && baseValue is JsonObject baseChild)
            {
                var childDiff = Diff(baseChild, targetChild);
                if (childDiff.Count > 0)
                    diff[name] = childDiff;
            }
            else if (!hasBase || !JsonNode.DeepEquals(baseValue, value))
            {
                diff[name] = value?.DeepClone();
            }
        }

        return diff;
    }

    private static void MergeInto(JsonObject target, JsonObject overlay)
    {
        foreach (var (name, value) in overlay)
        {
            if (value is JsonObject overlayChild && target[name] is JsonObject targetChild)
                MergeInto(targetChild, overlayChild);
            else
                target[name] = value?.DeepClone();
        }
    }
}
