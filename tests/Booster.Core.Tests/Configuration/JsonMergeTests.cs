using System.Text.Json.Nodes;
using Booster.Core.Configuration;

namespace Booster.Core.Tests.Configuration;

public class JsonMergeTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Nested_objects_are_merged_recursively()
    {
        var merged = JsonMerge.Merge(Obj("""{"a":{"x":1,"y":2},"b":3}"""), Obj("""{"a":{"y":20,"z":30}}"""));

        Assert.True(JsonNode.DeepEquals(Obj("""{"a":{"x":1,"y":20,"z":30},"b":3}"""), merged));
    }

    [Fact]
    public void Array_in_overlay_replaces_base_array()
    {
        var merged = JsonMerge.Merge(Obj("""{"hosts":["a","b","c"]}"""), Obj("""{"hosts":["d"]}"""));

        Assert.True(JsonNode.DeepEquals(Obj("""{"hosts":["d"]}"""), merged));
    }

    [Fact]
    public void Null_in_overlay_replaces_value_with_null()
    {
        var merged = JsonMerge.Merge(Obj("""{"path":"C:\\pm.exe","n":1}"""), Obj("""{"path":null}"""));

        Assert.True(merged.ContainsKey("path"));
        Assert.Null(merged["path"]);
        Assert.Equal(1, merged["n"]!.GetValue<int>());
    }

    [Fact]
    public void Inputs_are_not_mutated()
    {
        var baseDoc = Obj("""{"a":{"x":1},"list":[1,2]}""");
        var overlay = Obj("""{"a":{"x":2,"y":3},"list":[9]}""");
        var baseBefore = baseDoc.ToJsonString();
        var overlayBefore = overlay.ToJsonString();

        var merged = JsonMerge.Merge(baseDoc, overlay);
        merged["a"]!["x"] = 100;

        Assert.Equal(baseBefore, baseDoc.ToJsonString());
        Assert.Equal(overlayBefore, overlay.ToJsonString());
    }

    [Fact]
    public void Diff_of_equal_documents_is_empty()
    {
        var doc = Obj("""{"a":{"x":1},"list":[1,2],"s":"v","n":null}""");

        Assert.Empty(JsonMerge.Diff(doc, doc.DeepClone().AsObject()));
    }

    [Fact]
    public void Diff_keeps_only_changed_nested_values()
    {
        var diff = JsonMerge.Diff(Obj("""{"a":{"x":1,"y":2},"b":3}"""), Obj("""{"a":{"x":1,"y":20},"b":3}"""));

        Assert.True(JsonNode.DeepEquals(Obj("""{"a":{"y":20}}"""), diff), diff.ToJsonString());
    }

    [Fact]
    public void Diff_takes_changed_array_whole()
    {
        var diff = JsonMerge.Diff(Obj("""{"hosts":["a","b"]}"""), Obj("""{"hosts":["a","c"]}"""));

        Assert.True(JsonNode.DeepEquals(Obj("""{"hosts":["a","c"]}"""), diff), diff.ToJsonString());
    }

    [Fact]
    public void Diff_includes_new_properties_and_null_replacing_value()
    {
        var diff = JsonMerge.Diff(Obj("""{"path":"C:\\pm.exe"}"""), Obj("""{"path":null,"extra":{"k":1}}"""));

        Assert.True(JsonNode.DeepEquals(Obj("""{"path":null,"extra":{"k":1}}"""), diff), diff.ToJsonString());
    }

    [Fact]
    public void Diff_treats_equal_numbers_as_equal_regardless_of_spelling()
    {
        Assert.Empty(JsonMerge.Diff(Obj("""{"n":1000}"""), Obj("""{"n":1000}""")));
        Assert.Single(JsonMerge.Diff(Obj("""{"n":1000}"""), Obj("""{"n":1001}""")));
    }

    [Fact]
    public void Merging_diff_onto_base_restores_target()
    {
        var baseDoc = Obj("""{"a":{"x":1,"y":{"deep":true}},"list":[1],"s":"v"}""");
        var target = Obj("""{"a":{"x":2,"y":{"deep":false}},"list":[1],"s":"w","new":5}""");

        var restored = JsonMerge.Merge(baseDoc, JsonMerge.Diff(baseDoc, target));

        Assert.True(JsonNode.DeepEquals(target, restored), restored.ToJsonString());
    }

    [Fact]
    public void Diff_does_not_mutate_inputs_or_share_nodes()
    {
        var baseDoc = Obj("""{"a":{"x":1}}""");
        var target = Obj("""{"a":{"x":2},"list":[9]}""");
        var targetBefore = target.ToJsonString();

        var diff = JsonMerge.Diff(baseDoc, target);
        diff["list"]!.AsArray().Add(10);

        Assert.Equal("""{"a":{"x":1}}""", baseDoc.ToJsonString());
        Assert.Equal(targetBefore, target.ToJsonString());
    }
}
