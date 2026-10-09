using System.Security.Cryptography;

namespace Compositor.Core;

public static class SignedUpdate {
    public static void Verify(byte[] bytes, byte[] signature, string pem) {
        try { using var rsa = RSA.Create(); rsa.ImportFromPem(pem); if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new CryptographicException(); }
        catch (Exception e) when (e is CryptographicException or ArgumentException) { throw new OperationException(ErrorCode.SignatureFailed, "", e); }
    }
    public static bool IsNewer(string offered, string current) {
        if (!Version.TryParse(offered, out var next) || !Version.TryParse(current, out var existing)) throw new OperationException(ErrorCode.InvalidMetadata, "版本格式无效。");
        return next > existing;
    }
    public static void VerifyPackage(byte[] bytes, long length, string hash) {
        if (bytes.LongLength != length || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase)) throw new OperationException(ErrorCode.IntegrityFailed);
    }
    public static async Task VerifyPackageFile(string path, long length, string hash, CancellationToken cancellation = default) {
        await using var stream = File.OpenRead(path);
        if (stream.Length != length || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)), hash, StringComparison.OrdinalIgnoreCase)) throw new OperationException(ErrorCode.IntegrityFailed);
    }
}
