param([Parameter(Mandatory)][string]$Version,[string]$Directory='native-artifacts')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$stage=[IO.Path]::GetFullPath((Join-Path $root $Directory))
if(!$env:WINDOWS_UPDATE_PRIVATE_KEY){throw '发布需要生产签名密钥'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$release=Join-Path $stage 'release';New-Item -ItemType Directory -Force $release | Out-Null
$payload=Join-Path $stage 'payload';$zipPath=Join-Path $release "Compositor-$Version-payload.zip"
if(Test-Path -LiteralPath $zipPath){throw '不覆盖已有同版本资产'}
$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
$files=@()
try{
 foreach($file in Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName){
  $relative=[IO.Path]::GetRelativePath($payload,$file.FullName).Replace('\','/')
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,('payload/'+$relative),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
  $files += [ordered]@{path=$relative;size=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
 }
}finally{$zip.Dispose()}
$manifest=[ordered]@{schema=1;product='compositor-windows';channel='stable';version=$Version;files=$files;bundle=[ordered]@{size=(Get-Item $zipPath).Length;sha256=(Get-FileHash $zipPath).Hash.ToLowerInvariant()}}
$encoding=[Text.UTF8Encoding]::new($false);$bytes=$encoding.GetBytes(($manifest | ConvertTo-Json -Depth 8 -Compress))
$rsa=[Security.Cryptography.RSA]::Create()
try{
 $rsa.ImportFromPem($env:WINDOWS_UPDATE_PRIVATE_KEY)
 $signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
}finally{$rsa.Dispose()}
$envelope=[ordered]@{keyId='sun-dove-compositor-rsa3072-v1';payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)}
$feed=Join-Path $release 'native-feed.json';[IO.File]::WriteAllText($feed,($envelope | ConvertTo-Json -Compress),$encoding)
$localFeed=Join-Path $stage 'feed';New-Item -ItemType Directory -Force $localFeed | Out-Null
Copy-Item -LiteralPath $feed -Destination (Join-Path $localFeed 'feed.json')
Copy-Item -LiteralPath $payload -Destination (Join-Path $localFeed 'payload') -Recurse
$sandbox=Join-Path $stage 'install-gate';New-Item -ItemType Directory -Path $sandbox | Out-Null
Get-ChildItem -LiteralPath (Join-Path $stage 'bootstrap') | Copy-Item -Destination $sandbox -Recurse
$process=Start-Process (Join-Path $sandbox 'CompositorStart.exe') -ArgumentList @('--initialize','--install','--quiet','--feed',('"'+$localFeed+'"')) -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(180000) -or $process.ExitCode -ne 0){throw '签名全文件校验与新版本健康安装门失败'}
$active=Get-Content (Join-Path $sandbox 'state/active.json') -Raw | ConvertFrom-Json
if($active.current -ne $Version -or $active.pending){throw '安装状态不正确'}
$notes="Windows 原界面中文预览 $Version。原 Qt 控件、图标、Inter 字体保留，启动检查生产签名更新。源码：$(& git -C $root rev-parse HEAD)。保留参考项目已知限制，不宣称最新 Mac 全功能一致。"
[IO.File]::WriteAllText((Join-Path $release '发布说明.md'),$notes,$encoding)
$compiler='C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
if(!(Test-Path $compiler)){throw 'Windows runner 缺少 Inno Setup 6，未生成安装包'}
& $compiler "/DProductVersion=$Version" "/DStage=$stage" (Join-Path $root 'product/Setup.iss')
if($LASTEXITCODE){throw '安装包编译失败'}
Get-ChildItem -LiteralPath $release -File | Where-Object {$_.Name -ne 'SHA256SUMS'} | ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $($_.Name)"} | Set-Content (Join-Path $release 'SHA256SUMS') -Encoding utf8
[ordered]@{status='passed';version=$Version;sourceCommit=(& git -C $root rev-parse HEAD);signature='RSA3072-SHA256-PKCS1';healthCheck=0;payloadFiles=$files.Count} | ConvertTo-Json | Set-Content (Join-Path $stage 'evidence/install-gate.json') -Encoding utf8
