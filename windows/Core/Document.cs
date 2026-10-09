using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed class LayerData(JsonObject record, byte[]? png) {
    public JsonObject Record { get; } = record;
    public byte[]? Png { get; } = png;
    public Guid Id => Guid.Parse(Record["id"]!.GetValue<string>());
    public string Name { get => Record["name"]!.GetValue<string>(); set => Record["name"] = value; }
    public bool Visible { get => Record["isVisible"]?.GetValue<bool>() ?? true; set => Record["isVisible"] = value; }
    public bool IsGroup => Record["isGroup"]?.GetValue<bool>() ?? false;
    public Guid? Parent => Record["parentID"] is JsonValue p ? Guid.Parse(p.GetValue<string>()) : null;
    public double Opacity { get => Record["opacity"]?.GetValue<double>() ?? 1; set => Record["opacity"] = value; }
    public string Blend { get => Record["blendMode"]?.GetValue<string>() ?? "Normal"; set => Record["blendMode"] = value; }
    public JsonObject Transform => Record["transform"]!.AsObject();
    public double OriginX => Transform["origin"]![0]!.GetValue<double>();
    public double OriginY => Transform["origin"]![1]!.GetValue<double>();
    public double Width => Transform["size"]![0]!.GetValue<double>();
    public double Height => Transform["size"]![1]!.GetValue<double>();
    public double Rotation => Transform["rotation"]?.GetValue<double>() ?? 0;
    public bool FlipX => Transform["flipX"]?.GetValue<bool>() ?? false;
    public bool FlipY => Transform["flipY"]?.GetValue<bool>() ?? false;
    public void Move(double x, double y) => Transform["origin"] = new JsonArray(x, y);
    public LayerData Clone() => new((JsonObject)Record.DeepClone(), Png);
}

public sealed class DocumentData(JsonObject manifest, List<LayerData> layers) {
    public JsonObject Manifest { get; } = manifest;
    public List<LayerData> Layers { get; } = layers;
    public Guid Id => Guid.Parse(Manifest["documentID"]!.GetValue<string>());
    public int Width => Manifest["width"]!.GetValue<int>();
    public int Height => Manifest["height"]!.GetValue<int>();
    public Guid? ActiveId { get => Manifest["activeLayerID"] is JsonValue p ? Guid.Parse(p.GetValue<string>()) : null; set => Manifest["activeLayerID"] = value?.ToString().ToUpperInvariant(); }
    public DocumentData Clone() => new((JsonObject)Manifest.DeepClone(), Layers.Select(l => l.Clone()).ToList());
    public JsonObject Serialize() {
        var result = (JsonObject)Manifest.DeepClone();
        result["layers"] = new JsonArray(Layers.Select(l => (JsonNode)l.Record.DeepClone()).ToArray());
        return result;
    }
    public static DocumentData Create(int width, int height) {
        Raster.CheckSize(width, height);
        return new(new JsonObject { ["format"] = "com.compositor.project", ["version"] = 3, ["colorSpace"] = "sRGB", ["documentID"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["width"] = width, ["height"] = height, ["resolution"] = 72, ["layers"] = new JsonArray() }, []);
    }
}
