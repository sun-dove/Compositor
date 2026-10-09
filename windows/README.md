# Compositor Windows 中文版

新增的 Windows 基础图层编辑器，保留原作者的 Mac 工程。界面参照原版：中性深灰、左侧工具栏、顶部变换栏、右侧图层面板与底部状态栏。英文使用 Windows 系统 Segoe UI，中文回退微软雅黑 UI；原版图标转换为 Windows ICO，保留仓库 MIT 许可证。

## 使用

从本仓库带 `windows-v` 前缀的 GitHub Release 下载 `CompositorWindows-win-Setup.exe`。使用安装版才能获得自动更新；Portable ZIP 仅用于免安装运行。

首版针对 Windows x64。本任务在用户当前 Windows x64 环境实际验收，不宣称已覆盖其他架构或所有 Windows 版本。自包含 .NET 运行时，无需单独安装 .NET SDK。

菜单“文件”提供新建、打开、导入 PNG/JPEG、保存、另存为和导出透明 PNG。“编辑”支持撤销/重做；图层支持名称、显示、不透明度、八种混合模式、拖动移动、坐标位置及顺序调整。

`.comp` 为目录包，打开时选择 `.comp` 文件夹。支持格式 1–11 的基础栅格/目录子集，保留图像原始 PNG 和已支持元数据。文字、形状、蒙版、效果、调整层、参考线及未知字段会拒绝打开，避免悄悄丢失效果。工具栏未适配工具显示为灰色。此软件尚未实现 Mac 全部编辑能力，也未验证 Mac→Windows→Mac 的全面往返。

启动后检查自己的签名更新源。发现新版本后，在“帮助”中下载更新，完成后保存项目、安装并重启；失败显示中文原因，可继续使用当前版。软件使用 RSA-PSS/SHA-256 验证元数据，并再次核对包大小与哈希；不自动执行未经校验的缓存包。

更新签名不等于 Windows Authenticode 签名：当前安装程序没有商业代码签名证书，首次安装可能显示系统信誉提示。

## 跟随上游

`.github/workflows/windows-release.yml` 每小时检查原作者 main；无变化不发布。兼容源码更新在独立候选分支合并，验收、安装升级和打包成功后才发布 Windows Release，最后更新签名索引。冲突、发布脚本/工作流/关键格式变化或验收失败会停止发布并保留旧版。

GitHub 定时运行可能延迟，长期无活动的 fork 定时任务可能被 GitHub 停用。“同步源码”不会自动将作者新增 Swift/Metal 功能移植到 Windows；新增功能仍须设计、场景与验收后适配。

需要发布自己的修复时，在 Actions 的“自动同步并发布 Windows 中文版”中手动运行，勾选 `force_release`。签名私钥保存于 `WINDOWS_UPDATE_PRIVATE_KEY` Secret，不得上传到源码或聊天。

## 开发与正式验收

使用 .NET SDK 8.0.425。依赖版本和锁文件已固定。

```powershell
./scripts/windows/build.ps1 -Output windows/artifacts/verify
python scripts/windows/verify-sync.py
./scripts/windows/build.ps1 -Version 0.1.123 -Output windows/artifacts/release -Package
```

`windows/Gates`、应用 `--ui-gates`、`--upgrade-gate`、`--upgrade-local-gate` 和 `scripts/windows/verify-*.py/ps1` 是正式验收交付物。局部更新源仅通过显式验收参数启用；普通启动固定使用自己的 GitHub 签名索引，不读取这些验收目录。

实际版本、通过的门与验证局限参见 `docs/windows/交付与验收.md`。
