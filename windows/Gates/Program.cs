using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Compositor.Core;

var failures = new List<string>();
var root = Path.Combine(Path.GetTempPath(), "Compositor正式验收-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var count = 0;
void Gate(string name, Action action) {
    count++;
    try { action(); Console.WriteLine("通过 " + name); }
    catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("失败 " + failures[^1]); }
}
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
void Error(ErrorCode code, Action action) {
    try { action(); throw new Exception("未拒绝非法输入"); }
    catch (OperationException e) { Assert(e.Code == code, $"错误应为 {code}，实际 {e.Code}"); }
}
try {
    Gate("S02 新建、合成、移动与历史", () => {
        var editor = new EditorSession();
        editor.New(8, 8);
        editor.AddImage(Raster.SolidPng(4, 4, 255, 0, 0), "红色");
        editor.MoveActive(2, 2);
        var pixels = Raster.Pixel(editor.ExportPng(), 3, 3);
        Assert(pixels.R == 255 && pixels.A == 255, "导出像素不符");
        Assert(Raster.Pixel(editor.ExportPng(), 0, 0).A == 0, "透明区域被改变");
        editor.Undo(); Assert(editor.Active!.OriginX == 0, "撤销未恢复变换");
        editor.Redo(); Assert(editor.Active!.OriginX == 2, "重做未恢复变换");
    });
    Gate("S03 中文路径、透明图层与项目往返", () => {
        var editor = new EditorSession(); editor.New(4, 4);
        editor.AddImage(Raster.SolidPng(4, 4, 255, 0, 0), "中文图层"); editor.SetOpacity(.5);
        var path = Path.Combine(root, "中文目录", "合成作品.comp");
        editor.Save(path);
        var before = editor.ExportPng();
        var loaded = new EditorSession(); loaded.Open(path);
        Assert(before.SequenceEqual(loaded.ExportPng()), "重开后像素改变");
        Assert(loaded.Active!.Name == "中文图层", "中文名字丢失");
        Assert(!loaded.Dirty && !loaded.CanUndo, "打开后状态不正确");
    });
    Gate("S04 各类失败不替换当前文档", () => {
        var editor = new EditorSession(); editor.New(4, 4); var id = editor.Document!.Id;
        var valid = Path.Combine(root, "错误场景.comp"); editor.Save(valid);
        var manifest = Path.Combine(valid, "manifest.json");
        var original = File.ReadAllText(manifest);
        File.WriteAllText(manifest, "{"); Error(ErrorCode.InvalidManifest, () => editor.Open(valid));
        Assert(editor.Document!.Id == id, "非法项目替换了文档");
        var node = JsonNode.Parse(original)!.AsObject(); node["version"] = 999;
        File.WriteAllText(manifest, node.ToJsonString()); Error(ErrorCode.UnsupportedVersion, () => editor.Open(valid));
        File.WriteAllText(manifest, original);
        editor.AddImage(Raster.SolidPng(1, 1, 0, 0, 255), "蓝色"); editor.Save(valid);
        node = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
        node["layers"]![0]!["blendMode"] = "Color Dodge";
        File.WriteAllText(manifest, node.ToJsonString()); Error(ErrorCode.UnsupportedFeature, () => editor.Open(valid));
        node["layers"]![0]!["blendMode"] = "Normal"; node["layers"]![0]!["imageFile"] = "../outside.png";
        File.WriteAllText(manifest, node.ToJsonString()); Error(ErrorCode.UnsafePath, () => editor.Open(valid));
        Assert(editor.Document!.Id == id, "失败改变了当前文档");
    });
    Gate("S03 保存失败保留原项目", () => {
        var editor = new EditorSession(); editor.New(4, 4);
        var path = Path.Combine(root, "保存失败.comp"); editor.Save(path);
        var original = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
        editor.AddImage(Raster.SolidPng(2, 2, 0, 255, 0), "修改");
        Error(ErrorCode.SaveFailed, () => ProjectStore.Save(editor.Document!, path, () => throw new IOException("注入替换前失败")));
        Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(path, "manifest.json"))), "旧项目被破坏");
        Assert(editor.Dirty, "保存失败却消除了未保存标志");
    });
    Gate("S02 图层顺序、隐藏与混合", () => {
        var editor = new EditorSession(); editor.New(2, 2);
        editor.AddImage(Raster.SolidPng(2, 2, 255, 0, 0), "底部");
        editor.AddImage(Raster.SolidPng(2, 2, 0, 0, 255), "顶部");
        Assert(Raster.Pixel(editor.ExportPng(), 0, 0).B == 255, "图层顺序反了");
        editor.SetVisible(false); Assert(Raster.Pixel(editor.ExportPng(), 0, 0).R == 255, "隐藏未生效");
        editor.SetVisible(true); editor.SetBlend("Multiply");
        var p = Raster.Pixel(editor.ExportPng(), 0, 0); Assert(p.R == 0 && p.B == 0, "正片叠底不符");
    });
    Gate("U01 数字签名、篡改与版本比较", () => {
        using var key = RSA.Create(3072);
        var bytes = "{\"version\":\"0.1.12\"}"u8.ToArray();
        var sig = key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        SignedUpdate.Verify(bytes, sig, key.ExportSubjectPublicKeyInfoPem());
        Error(ErrorCode.SignatureFailed, () => SignedUpdate.Verify("changed"u8.ToArray(), sig, key.ExportSubjectPublicKeyInfoPem()));
        Assert(SignedUpdate.IsNewer("0.1.12", "0.1.9"), "版本被按字符串比较");
        Assert(!SignedUpdate.IsNewer("0.1.9", "0.1.12"), "允许版本降级");
    });
    Gate("U02 包完整性和大小校验", () => {
        var bytes = "完整更新包"u8.ToArray();
        SignedUpdate.VerifyPackage(bytes, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        Error(ErrorCode.IntegrityFailed, () => SignedUpdate.VerifyPackage("坏包"u8.ToArray(), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
    });
    Gate("U01 完整签名清单、架构与包路径", () => {
        using var key = RSA.Create(3072);
        var payload = new JsonObject { ["schemaVersion"] = 1, ["architecture"] = "X64", ["channel"] = "win", ["version"] = "0.1.12", ["feed"] = new JsonObject { ["Assets"] = new JsonArray(new JsonObject { ["PackageId"] = "CompositorWindows", ["Version"] = "0.1.12", ["Type"] = "Full", ["FileName"] = "CompositorWindows-0.1.12-full.nupkg", ["Size"] = 12L, ["SHA256"] = new string('A', 64) }) } };
        byte[] Envelope() {
            var bytes = System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString());
            return System.Text.Encoding.UTF8.GetBytes(new JsonObject { ["payload"] = Convert.ToBase64String(bytes), ["signature"] = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) }.ToJsonString());
        }
        var release = UpdateManifest.Read(Envelope(), key.ExportSubjectPublicKeyInfoPem());
        Assert(release.Version == "0.1.12" && release.PackageUrl.StartsWith("https://github.com/sun-dove/Compositor/releases/download/windows-v0.1.12/"), "合法清单与固定更新源不一致");
        payload["architecture"] = "Arm64"; Error(ErrorCode.InvalidMetadata, () => UpdateManifest.Read(Envelope(), key.ExportSubjectPublicKeyInfoPem()));
        payload["architecture"] = "X64"; payload["feed"]!["Assets"]![0]!["FileName"] = "../escape-full.nupkg";
        Error(ErrorCode.InvalidMetadata, () => UpdateManifest.Read(Envelope(), key.ExportSubjectPublicKeyInfoPem()));
    });
    Gate("S02 目录连续绘制、透明度与保存往返", () => {
        var editor = new EditorSession(); editor.New(2, 2);
        editor.AddImage(Raster.SolidPng(2, 2, 255, 0, 0), "根红色"); var red = editor.Active!;
        editor.AddImage(Raster.SolidPng(2, 2, 0, 0, 255), "目录蓝色"); var blue = editor.Active!;
        var groupId = Guid.NewGuid(); var record = (JsonObject)red.Record.DeepClone();
        record["id"] = groupId.ToString().ToUpperInvariant(); record["name"] = "目录"; record["isGroup"] = true; record["opacity"] = .5; record.Remove("imageFile");
        blue.Record["parentID"] = groupId.ToString().ToUpperInvariant(); editor.Document!.Layers.Insert(0, new LayerData(record, null)); editor.Document.Manifest["version"] = 8;
        Assert(Raster.Pixel(editor.ExportPng(), 0, 0).R == 255, "目录子树没有按连续顺序合成");
        red.Visible = false; var pixel = Raster.Pixel(editor.ExportPng(), 0, 0); Assert(pixel.B == 255 && pixel.A == 128, "目录透明度不符");
        var path = Path.Combine(root, "目录往返.comp"); editor.Save(path); var before = editor.ExportPng(); editor.Open(path);
        Assert(before.SequenceEqual(editor.ExportPng()), "目录重开后像素改变");
    });
    Gate("S04 缺失资源与未知效果拒绝打开", () => {
        var editor = new EditorSession(); editor.New(2, 2); editor.AddImage(Raster.SolidPng(2, 2, 255, 0, 0), "原图");
        var path = Path.Combine(root, "缺失资源.comp"); editor.Save(path); var manifestFile = Path.Combine(path, "manifest.json");
        var node = JsonNode.Parse(File.ReadAllText(manifestFile))!; node["layers"]![0]!["effects"] = new JsonObject { ["unknownEffect"] = true };
        File.WriteAllText(manifestFile, node.ToJsonString()); Error(ErrorCode.UnsupportedFeature, () => editor.Open(path));
        node["layers"]![0]!.AsObject().Remove("effects"); File.WriteAllText(manifestFile, node.ToJsonString());
        File.Delete(Path.Combine(path, "images", editor.Active!.Record["imageFile"]!.GetValue<string>()));
        var id = editor.Document!.Id; Error(ErrorCode.MissingAsset, () => editor.Open(path)); Assert(editor.Document!.Id == id, "资源失败替换了当前文档");
    });
} finally {
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
Console.WriteLine($"全部执行：{count} 门；失败：{failures.Count}；未抽样、未跳过。");
return failures.Count == 0 ? 0 : 1;
