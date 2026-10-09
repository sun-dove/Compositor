param(
    [string]$Installer,
    [string]$InstalledExe,
    [Parameter(Mandatory)][string]$InitialVersion,
    [Parameter(Mandatory)][string]$TargetVersion,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [string]$SignedFeedDirectory,
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$report = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $report -Force | Out-Null
$mode = if ($SignedFeedDirectory) { '--upgrade-local-gate' } else { '--upgrade-gate' }
$appArgs = @($mode, ('"'+$report+'"'))
if ($SignedFeedDirectory) { $appArgs += ('"'+[IO.Path]::GetFullPath($SignedFeedDirectory)+'"') }
if ($Installer) {
    $install = Join-Path $report '安装目录'
    if (Test-Path -LiteralPath $install) { throw '安装验收必须使用全新的唯一目录，避免覆盖已有用户安装' }
    $setupArgs = @('--silent', '--installto', ('"'+$install+'"'), '--log', ('"'+(Join-Path $report '安装日志.log')+'"'))
    $process = Start-Process -FilePath ([IO.Path]::GetFullPath($Installer)) -ArgumentList $setupArgs -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw '初次安装失败，停止升级验收' }
    $process = Start-Process -FilePath (Join-Path $install 'current/Compositor.Windows.exe') -ArgumentList $appArgs -WindowStyle Hidden -PassThru
} elseif ($InstalledExe) {
    $process = Start-Process -FilePath ([IO.Path]::GetFullPath($InstalledExe)) -ArgumentList $appArgs -WindowStyle Hidden -PassThru
} else { throw '必须提供安装包或已安装的程序路径' }
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$targetReport = Join-Path $report ('version-'+$TargetVersion+'.json')
while (!(Test-Path -LiteralPath $targetReport)) {
    if (Test-Path -LiteralPath (Join-Path $report 'upgrade-failed.json')) { throw ('升级失败：'+(Get-Content -LiteralPath (Join-Path $report 'upgrade-failed.json') -Raw)) }
    if ([DateTime]::UtcNow -gt $deadline) { throw '安装、下载或重启升级验收超时；没有将超时当作成功' }
    Start-Sleep -Milliseconds 500
}
foreach ($version in @($InitialVersion, $TargetVersion)) {
    $result = Get-Content -LiteralPath (Join-Path $report ('version-'+$version+'.json')) -Raw | ConvertFrom-Json
    if ($result.installed -ne $version -or !$result.documentVerified) { throw ('版本或升级后项目不一致：'+$version) }
}
Write-Output ('实际安装与重启升级通过：'+$InitialVersion+' → '+$TargetVersion+'；中文项目与像素保持一致。')
