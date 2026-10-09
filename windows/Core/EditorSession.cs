using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed class EditorSession {
    private readonly Stack<DocumentData> undo = new();
    private readonly Stack<DocumentData> redo = new();
    private string savedState = "";
    public DocumentData? Document { get; private set; }
    public string? ProjectPath { get; private set; }
    public LayerData? Active => Document?.Layers.Find(l => l.Id == Document.ActiveId);
    public bool Dirty => Document != null && Fingerprint() != savedState;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void New(int width, int height) { var next = DocumentData.Create(width, height); Document = next; ProjectPath = null; undo.Clear(); redo.Clear(); savedState = ""; }
    public void Open(string path) { var next = ProjectStore.Load(path); Document = next; ProjectPath = path; undo.Clear(); redo.Clear(); savedState = Fingerprint(); }
    public void Save(string path) { RequireDocument(); ProjectStore.Save(Document!, path); ProjectPath = path; savedState = Fingerprint(); }
    public void Select(Guid id) { if (Document?.Layers.Any(l => l.Id == id) == true) Document.ActiveId = id; }
    public void AddImage(byte[] bytes, string name) {
        RequireDocument();
        var imported = Raster.Import(bytes);
        long used = (long)imported.Width * imported.Height;
        foreach (var existing in Document!.Layers.Where(l => l.Png != null)) { using var stream = new SkiaSharp.SKMemoryStream(existing.Png!); using var codec = SkiaSharp.SKCodec.Create(stream); used += (long)codec.Info.Width * codec.Info.Height; }
        if (used > Raster.PixelLimit) throw new OperationException(ErrorCode.DocumentTooLarge);
        Begin(); var id = Guid.NewGuid();
        Document.Layers.Add(new(new JsonObject { ["id"] = id.ToString().ToUpperInvariant(), ["name"] = string.IsNullOrWhiteSpace(name) ? "新图层" : name, ["isVisible"] = true, ["imageFile"] = id.ToString().ToUpperInvariant() + ".png", ["opacity"] = 1d, ["blendMode"] = "Normal", ["transform"] = new JsonObject { ["origin"] = new JsonArray(0d, 0d), ["size"] = new JsonArray((double)imported.Width, (double)imported.Height), ["rotation"] = 0d, ["flipX"] = false, ["flipY"] = false, ["sampling"] = "High quality" } }, imported.Png));
        Document.ActiveId = id;
    }
    public void MoveActive(double x, double y) { if (Active == null || !double.IsFinite(x) || !double.IsFinite(y) || (Active.OriginX == x && Active.OriginY == y)) return; Begin(); Active.Move(x, y); }
    public void SetOpacity(double value) { if (Active == null || !double.IsFinite(value) || value is < 0 or > 1 || Active.Opacity == value) return; Begin(); Active.Opacity = value; Document!.Manifest["version"] = Math.Max(Document.Manifest["version"]!.GetValue<int>(), Active.IsGroup ? 8 : 3); }
    public void SetVisible(bool value) { if (Active == null || Active.Visible == value) return; Begin(); Active.Visible = value; }
    public void SetBlend(string value) { if (Active == null || Active.IsGroup || !Raster.Blends.ContainsKey(value) || Active.Blend == value) return; Begin(); Active.Blend = value; Document!.Manifest["version"] = Math.Max(Document.Manifest["version"]!.GetValue<int>(), 3); }
    public void Rename(string name) { if (Active == null || string.IsNullOrWhiteSpace(name) || name == Active.Name) return; Begin(); Active.Name = name; }
    public void DeleteActive() {
        if (Active == null) return;
        if (Active.IsGroup && Document!.Layers.Any(l => l.Parent == Active.Id)) throw new OperationException(ErrorCode.UnsupportedFeature, "请先删除文件夹中的图层。");
        Begin(); Document!.Layers.Remove(Active); Document.ActiveId = Document.Layers.LastOrDefault()?.Id;
    }
    public void ReorderActive(int delta) { if (Active == null || Active.IsGroup || Active.Parent != null) return; var index = Document!.Layers.IndexOf(Active); var target = Math.Clamp(index + delta, 0, Document.Layers.Count - 1); if (index == target || Document.Layers[target].Parent != null || Document.Layers[target].IsGroup) return; Begin(); var layer = Active!; Document.Layers.RemoveAt(index); Document.Layers.Insert(target, layer); }
    public void Undo() { if (undo.Count == 0) return; redo.Push(Document!.Clone()); Document = undo.Pop(); }
    public void Redo() { if (redo.Count == 0) return; undo.Push(Document!.Clone()); Document = redo.Pop(); }
    public byte[] ExportPng() { RequireDocument(); return Raster.Render(Document!); }
    private void Begin() { RequireDocument(); undo.Push(Document!.Clone()); redo.Clear(); if (undo.Count > 30) { var keep = undo.Take(30).Reverse().ToArray(); undo.Clear(); foreach (var item in keep) undo.Push(item); } }
    private void RequireDocument() { if (Document == null) throw new OperationException(ErrorCode.InvalidManifest, "请先新建或打开项目。"); }
    private string Fingerprint() { var node = Document!.Serialize(); node.Remove("activeLayerID"); return node.ToJsonString(); }
}
