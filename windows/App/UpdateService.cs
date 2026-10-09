using System.Net;
using System.Reflection;
using Compositor.Core;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace Compositor.Windows;

internal sealed class SignedGithubSource : IUpdateSource {
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(15) };
    private VerifiedRelease? release;
    internal string? Version => release?.Version;
    private readonly string publicKey;
    private readonly string? gateDirectory;
    internal SignedGithubSource() {
        var args = Program.Arguments;
        if (args.Length == 3 && args[0] == "--upgrade-local-gate") gateDirectory = Path.GetFullPath(args[2]);
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("CompositorWindows/0.1");
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(s => s.EndsWith("update-public-key.pem")))!;
        using var reader = new StreamReader(stream); publicKey = reader.ReadToEnd();
    }
    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) {
        try {
            if (gateDirectory != null) {
                release = UpdateManifest.Read(await File.ReadAllBytesAsync(Path.Combine(gateDirectory, "stable.json")), publicKey);
                return VelopackAssetFeed.FromJson(release.FeedJson);
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var response = await Client.GetAsync("https://raw.githubusercontent.com/sun-dove/Compositor/windows-update/stable.json", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new OperationException(ErrorCode.InvalidMetadata);
            await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await input.ReadAsync(buffer, cts.Token)) > 0) { if (bytes.Length + read > 2 * 1024 * 1024) throw new OperationException(ErrorCode.InvalidMetadata); bytes.Write(buffer, 0, read); }
            release = UpdateManifest.Read(bytes.ToArray(), publicKey);
            return VelopackAssetFeed.FromJson(release.FeedJson);
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { throw new OperationException(ErrorCode.NetworkUnavailable, "", e); }
    }
    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset asset, string localFile, Action<int> progress, CancellationToken cancelToken = default) {
        var approved = release ?? throw new OperationException(ErrorCode.InvalidMetadata);
        if (asset.FileName != approved.PackageName || asset.Size != approved.PackageSize || !asset.SHA256.Equals(approved.Sha256, StringComparison.OrdinalIgnoreCase)) throw new OperationException(ErrorCode.InvalidMetadata);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(localFile))!);
        if (drive.AvailableFreeSpace < approved.PackageSize * 3 + 100L * 1024 * 1024) throw new OperationException(ErrorCode.InsufficientDiskSpace);
        try {
            if (gateDirectory != null) {
                await using var localInput = File.OpenRead(Path.Combine(gateDirectory, approved.PackageName));
                await using (var output = File.Create(localFile)) await localInput.CopyToAsync(output, cancelToken);
                await SignedUpdate.VerifyPackageFile(localFile, approved.PackageSize, approved.Sha256, cancelToken);
                progress(100); return;
            }
            using var response = await Client.GetAsync(approved.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancelToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancelToken);
            await using (var output = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true)) {
                var buffer = new byte[65536]; long written = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancelToken)) > 0) { written += read; if (written > approved.PackageSize) throw new OperationException(ErrorCode.IntegrityFailed); await output.WriteAsync(buffer.AsMemory(0, read), cancelToken); progress((int)(written * 100 / approved.PackageSize)); }
            }
            await SignedUpdate.VerifyPackageFile(localFile, approved.PackageSize, approved.Sha256, cancelToken);
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { throw new OperationException(ErrorCode.NetworkUnavailable, "", e); }
        catch (IOException e) { throw new OperationException(ErrorCode.IntegrityFailed, "", e); }
    }
}

internal sealed class UpdateService {
    internal readonly SignedGithubSource Source = new();
    internal readonly UpdateManager Manager;
    internal UpdateInfo? Available;
    internal bool Ready;
    internal UpdateService() { Manager = new(Source, new UpdateOptions { AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = -1 }); }
    internal async Task Check() { Available = await Manager.CheckForUpdatesAsync(); Ready = false; }
    internal async Task Download(Action<int> progress) {
        var update = Available ?? throw new OperationException(ErrorCode.InvalidMetadata);
        await Manager.DownloadUpdatesAsync(update, progress);
        try { await VerifyDownloaded(); Ready = true; }
        catch (OperationException e) when (e.Code == ErrorCode.IntegrityFailed) {
            var path = Path.Combine(VelopackLocator.Current.PackagesDir!, update.TargetFullRelease.FileName);
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }
    private async Task VerifyDownloaded() {
        var asset = Available?.TargetFullRelease ?? throw new OperationException(ErrorCode.InvalidMetadata);
        var path = Path.Combine(VelopackLocator.Current.PackagesDir!, asset.FileName);
        await SignedUpdate.VerifyPackageFile(path, asset.Size, asset.SHA256);
    }
    internal async Task Install() {
        if (!Ready) throw new OperationException(ErrorCode.InvalidMetadata);
        await VerifyDownloaded();
        Manager.ApplyUpdatesAndRestart(Available!.TargetFullRelease);
    }
    internal async Task InstallForUpgradeGate(string directory) {
        if (!Ready) throw new OperationException(ErrorCode.InvalidMetadata);
        await VerifyDownloaded();
        var args = Program.Arguments;
        Manager.ApplyUpdatesAndRestart(Available!.TargetFullRelease, args.Length == 3 && args[0] == "--upgrade-local-gate" ? ["--upgrade-local-gate", directory, args[2]] : ["--upgrade-gate", directory]);
    }
}
