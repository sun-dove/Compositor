using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record VerifiedRelease(string Version, string PackageName, long PackageSize, string Sha256, string PackageUrl, string FeedJson, string Notes);

public static class UpdateManifest {
    public static VerifiedRelease Read(byte[] envelopeBytes, string publicKey, string architecture = "X64") {
        try {
            if (envelopeBytes.Length > 2 * 1024 * 1024) throw new OperationException(ErrorCode.InvalidMetadata);
            var envelope = JsonNode.Parse(envelopeBytes)!.AsObject();
            var payload = Convert.FromBase64String(envelope["payload"]!.GetValue<string>());
            var signature = Convert.FromBase64String(envelope["signature"]!.GetValue<string>());
            SignedUpdate.Verify(payload, signature, publicKey);
            var manifest = JsonNode.Parse(payload)!.AsObject();
            if (manifest["schemaVersion"]!.GetValue<int>() != 1 || manifest["architecture"]!.GetValue<string>() != architecture || manifest["channel"]!.GetValue<string>() != "win") throw new OperationException(ErrorCode.InvalidMetadata);
            var version = manifest["version"]!.GetValue<string>();
            if (!System.Version.TryParse(version, out var v) || v.Major < 0 || v.Minor < 0 || v.Build < 0 || v.Revision >= 0) throw new OperationException(ErrorCode.InvalidMetadata);
            var feed = manifest["feed"]!.AsObject(); var assets = feed["Assets"]!.AsArray();
            if (assets.Count != 1) throw new OperationException(ErrorCode.InvalidMetadata);
            var asset = assets[0]!;
            var name = asset["FileName"]!.GetValue<string>(); var size = asset["Size"]!.GetValue<long>();
            var hash = asset["SHA256"]!.GetValue<string>();
            if (asset["PackageId"]!.GetValue<string>() != "CompositorWindows" || asset["Version"]!.GetValue<string>() != version || asset["Type"]!.GetValue<string>() != "Full" || Path.GetFileName(name) != name || name.Contains('\\') || name.Contains('/') || !name.EndsWith("-full.nupkg", StringComparison.Ordinal) || size < 1 || size > 1024L * 1024 * 1024 || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new OperationException(ErrorCode.InvalidMetadata);
            var url = "https://github.com/sun-dove/Compositor/releases/download/windows-v" + version + "/" + Uri.EscapeDataString(name);
            return new(version, name, size, hash, url, feed.ToJsonString(), manifest["notes"]?.GetValue<string>() ?? "Windows 中文版更新");
        } catch (OperationException) { throw; }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException) { throw new OperationException(ErrorCode.InvalidMetadata, "", e); }
    }
}
