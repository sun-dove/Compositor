using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compositor.Core;

public static class ProjectStore {
    private static readonly HashSet<string> LayerFields = ["id", "name", "isVisible", "transform", "imageFile", "parentID", "isGroup", "opacity", "blendMode"];
    public static DocumentData Load(string path) {
        try {
            if (!Directory.Exists(path)) throw new OperationException(ErrorCode.InvalidManifest);
            RejectLink(path);
            var file = Path.Combine(path, "manifest.json"); RejectLink(file);
            if (!File.Exists(file) || new FileInfo(file).Length > 4 * 1024 * 1024) throw new OperationException(ErrorCode.InvalidManifest);
            var manifest = JsonNode.Parse(File.ReadAllBytes(file))?.AsObject() ?? throw new OperationException(ErrorCode.InvalidManifest);
            ValidateHeader(manifest);
            var layers = new List<LayerData>();
            var records = manifest["layers"]?.AsArray() ?? throw new OperationException(ErrorCode.InvalidManifest);
            if (records.Count > 10_000) throw new OperationException(ErrorCode.DocumentTooLarge);
            long usedPixels = 0;
            foreach (var item in records) {
                var record = item?.AsObject() ?? throw new OperationException(ErrorCode.InvalidManifest);
                ValidateRecord(record);
                byte[]? png = null;
                if (record["imageFile"] is JsonValue nameNode) {
                    var name = nameNode.GetValue<string>();
                    if (name != record["id"]!.GetValue<string>() + ".png") throw new OperationException(ErrorCode.UnsafePath, name);
                    var images = Path.Combine(path, "images"); RejectLink(images);
                    var imagePath = Path.Combine(images, name); RejectLink(imagePath);
                    if (!File.Exists(imagePath) || new FileInfo(imagePath).Length > 512L * 1024 * 1024) throw new OperationException(ErrorCode.MissingAsset, name);
                    var bytes = File.ReadAllBytes(imagePath);
                    var imported = Raster.Import(bytes, pngOnly: true);
                    usedPixels += (long)imported.Width * imported.Height;
                    if (usedPixels > Raster.PixelLimit) throw new OperationException(ErrorCode.DocumentTooLarge);
                    // Preserve original source pixels and PNG metadata instead of transcoding on load/save.
                    png = bytes;
                }
                layers.Add(new((JsonObject)record.DeepClone(), png));
            }
            var document = new DocumentData(manifest, layers); ValidateHierarchy(document); return document;
        } catch (OperationException) { throw; }
        catch (UnauthorizedAccessException e) { throw new OperationException(ErrorCode.AccessDenied, "", e); }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException or NullReferenceException) {
            throw new OperationException(ErrorCode.InvalidManifest, "", e);
        }
    }
    public static void Save(DocumentData document, string path, Action? beforeReplace = null) {
        var target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(target)!;
        var stage = Path.Combine(parent, ".compositor-stage-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(parent, ".compositor-rollback-" + Guid.NewGuid().ToString("N"));
        var movedOriginal = false;
        try {
            if (File.Exists(target)) throw new IOException("目标路径已是文件，请另选一个 .comp 目录。");
            if (Directory.Exists(target)) { RejectLink(target); _ = Load(target); }
            Directory.CreateDirectory(parent); Directory.CreateDirectory(Path.Combine(stage, "images"));
            foreach (var layer in document.Layers) if (layer.Png is not null && layer.Record["imageFile"] is JsonValue filename) {
                var name = filename.GetValue<string>();
                if (name != layer.Record["id"]!.GetValue<string>() + ".png") throw new OperationException(ErrorCode.UnsafePath);
                File.WriteAllBytes(Path.Combine(stage, "images", name), layer.Png);
            }
            File.WriteAllText(Path.Combine(stage, "manifest.json"), document.Serialize().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _ = Load(stage);
            beforeReplace?.Invoke();
            if (Directory.Exists(target)) { Directory.Move(target, backup); movedOriginal = true; }
            try { Directory.Move(stage, target); }
            catch { if (movedOriginal && !Directory.Exists(target)) Directory.Move(backup, target); throw; }
            // A cleanup failure after a successful replacement must not report that the document was not saved.
            if (Directory.Exists(backup)) try { Directory.Delete(backup, true); } catch (IOException) { }
        } catch (OperationException e) when (e.Code != ErrorCode.SaveFailed) { throw new OperationException(ErrorCode.SaveFailed, e.UserMessage, e); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new OperationException(ErrorCode.SaveFailed, e.Message, e); }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private static void RejectLink(string path) {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new OperationException(ErrorCode.UnsafePath, "符号链接或目录联接不作为项目资源读取。");
    }
    private static void ValidateHeader(JsonObject node) {
        if (node["format"]?.GetValue<string>() != "com.compositor.project") throw new OperationException(ErrorCode.InvalidManifest);
        var version = node["version"]?.GetValue<int>() ?? 0;
        if (version is < 1 or > 11) throw new OperationException(ErrorCode.UnsupportedVersion, $"项目版本：{version}；支持版本：1–11 的基础图层子集。");
        if (node["colorSpace"]?.GetValue<string>() != "sRGB" || !Guid.TryParse(node["documentID"]?.GetValue<string>(), out _)) throw new OperationException(ErrorCode.InvalidManifest);
        Raster.CheckSize(node["width"]!.GetValue<int>(), node["height"]!.GetValue<int>());
        if (node["resolution"] is JsonValue resolution && (!double.IsFinite(resolution.GetValue<double>()) || resolution.GetValue<double>() is < 1 or > 9600)) throw new OperationException(ErrorCode.InvalidManifest);
        if (node["guides"] is JsonArray guides && guides.Count != 0) throw new OperationException(ErrorCode.UnsupportedFeature, "参考线尚未适配。");
        foreach (var field in node) if (field.Key is not ("format" or "version" or "colorSpace" or "documentID" or "width" or "height" or "resolution" or "activeLayerID" or "layers" or "guides") && field.Value is not null)
            throw new OperationException(ErrorCode.UnsupportedFeature, "未知文档字段：" + field.Key);
    }
    private static void ValidateRecord(JsonObject node) {
        if (!Guid.TryParse(node["id"]?.GetValue<string>(), out _) || string.IsNullOrWhiteSpace(node["name"]?.GetValue<string>())) throw new OperationException(ErrorCode.InvalidManifest);
        foreach (var field in node) if (!LayerFields.Contains(field.Key) && field.Value is not null)
            throw new OperationException(ErrorCode.UnsupportedFeature, $"图层“{node["name"]}”：{field.Key}");
        var layer = new LayerData(node, null);
        if (layer.Transform["origin"]?.AsArray().Count != 2 || layer.Transform["size"]?.AsArray().Count != 2) throw new OperationException(ErrorCode.InvalidManifest);
        if (!Raster.Blends.ContainsKey(layer.Blend)) throw new OperationException(ErrorCode.UnsupportedFeature, "混合模式：" + layer.Blend);
        if (!double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1 || !double.IsFinite(layer.OriginX) || !double.IsFinite(layer.OriginY) || !double.IsFinite(layer.Width) || !double.IsFinite(layer.Height) || layer.Width <= 0 || layer.Height <= 0 || !double.IsFinite(layer.Rotation)) throw new OperationException(ErrorCode.InvalidManifest);
        var sampling = layer.Transform["sampling"]?.GetValue<string>() ?? "High quality";
        if (sampling is not ("High quality" or "Smooth" or "Nearest")) throw new OperationException(ErrorCode.UnsupportedFeature, "采样方式：" + sampling);
        foreach (var field in layer.Transform) if (field.Key is not ("origin" or "size" or "rotation" or "flipX" or "flipY" or "sampling") && field.Value is not null) throw new OperationException(ErrorCode.UnsupportedFeature, "图层变换：" + field.Key);
        if (layer.IsGroup && (node["imageFile"] != null || layer.Blend != "Normal")) throw new OperationException(ErrorCode.InvalidManifest);
        // Validate values even when a blank layer would not be rendered.
        _ = layer.Visible; _ = layer.FlipX; _ = layer.FlipY;
    }
    private static void ValidateHierarchy(DocumentData doc) {
        var map = new Dictionary<Guid, LayerData>();
        foreach (var layer in doc.Layers) if (!map.TryAdd(layer.Id, layer)) throw new OperationException(ErrorCode.InvalidManifest, "图层 ID 重复。");
        foreach (var layer in doc.Layers) {
            var seen = new HashSet<Guid> { layer.Id }; var parent = layer.Parent;
            while (parent is Guid id) {
                if (!seen.Add(id) || seen.Count > 65 || !map.TryGetValue(id, out var p) || !p.IsGroup) throw new OperationException(ErrorCode.InvalidManifest, "图层目录关系无效。");
                parent = p.Parent;
            }
        }
        if (doc.ActiveId is Guid active && !map.ContainsKey(active)) throw new OperationException(ErrorCode.InvalidManifest);
        var version = doc.Manifest["version"]!.GetValue<int>();
        if (version < 2 && doc.Layers.Any(l => l.IsGroup || l.Parent != null) || version < 3 && doc.Layers.Any(l => l.Opacity != 1 || l.Blend != "Normal") || version < 8 && doc.Layers.Any(l => l.IsGroup && l.Opacity != 1)) throw new OperationException(ErrorCode.InvalidManifest, "字段与格式版本不一致。");
    }
}
