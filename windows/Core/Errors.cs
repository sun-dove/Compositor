namespace Compositor.Core;

public enum ErrorCode { InvalidManifest, UnsupportedVersion, UnsupportedFeature, MissingAsset, UnsafePath, DocumentTooLarge, AccessDenied, SaveFailed, SignatureFailed, IntegrityFailed, NetworkUnavailable, InvalidMetadata, InsufficientDiskSpace }

public sealed class OperationException(ErrorCode code, string detail = "", Exception? inner = null) : Exception(detail, inner) {
    public ErrorCode Code { get; } = code;
    public string UserMessage => Messages.For(Code) + (string.IsNullOrWhiteSpace(Message) ? "" : "\n" + Message);
}

public static class Messages {
    public static string For(ErrorCode code) => code switch {
        ErrorCode.InvalidManifest => "项目元数据损坏或不符合格式，当前文档未被替换。",
        ErrorCode.UnsupportedVersion => "项目格式版本超出此 Windows 版支持范围，当前文档未被替换。",
        ErrorCode.UnsupportedFeature => "项目包含尚未支持的能力，已阻止打开和覆盖原文件。",
        ErrorCode.MissingAsset => "项目中的图片缺失或损坏，当前文档未被替换。",
        ErrorCode.UnsafePath => "项目包含不安全的资源路径，已拒绝读取。",
        ErrorCode.DocumentTooLarge => "文档超过当前 Windows 版的画布或内存限制。",
        ErrorCode.AccessDenied => "没有权限访问此位置，请选择可写目录。",
        ErrorCode.SaveFailed => "保存失败，原项目已保留。当前修改仍未保存。",
        ErrorCode.SignatureFailed => "更新签名不可信，已阻止安装。请重试或联系维护者。",
        ErrorCode.IntegrityFailed => "更新包不完整或已损坏，已阻止安装，可重试下载。",
        ErrorCode.NetworkUnavailable => "暂时无法检查或下载更新，请检查网络后重试。",
        ErrorCode.InvalidMetadata => "更新信息无效，已阻止安装，继续使用当前版本。",
        ErrorCode.InsufficientDiskSpace => "磁盘空间不足，请释放空间后重试。",
        _ => "操作失败，请重试。"
    };
}
