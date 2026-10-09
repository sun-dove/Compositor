param([Parameter(Mandatory)][string]$ReleaseDirectory, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$ProductCommit, [Parameter(Mandatory)][string]$UpstreamCommit)
$ErrorActionPreference = 'Stop'
if (!$env:WINDOWS_UPDATE_PRIVATE_KEY) { throw '缺少 WINDOWS_UPDATE_PRIVATE_KEY，禁止发布更新' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($env:WINDOWS_UPDATE_PRIVATE_KEY)
    if ($rsa.ExportSubjectPublicKeyInfoPem().Trim() -ne (Get-Content -LiteralPath (Join-Path $repoRoot 'windows/update-public-key.pem') -Raw).Trim()) { throw '签名私钥与软件内置公钥不匹配' }
    $feed = Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'releases.win.json') -Raw | ConvertFrom-Json
    $full = @($feed.Assets | Where-Object {$_.Type -eq 'Full' -and $_.Version -eq $Version})
    if ($full.Count -ne 1) { throw '当前版本必须恰好有一个完整更新包' }
    $package = Join-Path $ReleaseDirectory $full[0].FileName
    if (!(Test-Path -LiteralPath $package) -or (Get-Item -LiteralPath $package).Length -ne $full[0].Size) { throw '包大小与 feed 不一致' }
    $actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    if ($full[0].SHA256 -and $full[0].SHA256 -ne $actualHash) { throw '包哈希与 feed 不一致' }
    $full[0] | Add-Member -NotePropertyName SHA256 -NotePropertyValue $actualHash -Force
    $payload = [ordered]@{schemaVersion=1;version=$Version;productCommit=$ProductCommit;upstreamCommit=$UpstreamCommit;architecture='X64';channel='win';notes='Windows 中文基础图层版；已通过确定性门及中文体验门。';feed=@{Assets=@($full[0])}} | ConvertTo-Json -Depth 12 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    if (!$rsa.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)) {throw '签名自校验失败'}
    $envelope = @{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $ReleaseDirectory 'stable.json'),$envelope,[Text.UTF8Encoding]::new($false))
} finally {$rsa.Dispose()}
Write-Output '签名更新清单已生成并自校验；未输出私钥。'
