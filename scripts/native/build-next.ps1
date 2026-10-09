param([Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$native=Join-Path $root 'windows-native';$stage=Join-Path $root $Output
$vs=& 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
& (Join-Path $vs 'Common7/Tools/Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$cmake=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
& $cmake -S $native -B (Join-Path $native 'build/product') "-DPRODUCT_VERSION=$Version"
if($LASTEXITCODE){throw '后续版本配置失败'}
& $cmake --build (Join-Path $native 'build/product') --config Release --target CompositorProduct --parallel 4
if($LASTEXITCODE){throw '后续版本编译失败'}
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'native-artifacts/payload'),(Join-Path $root 'native-artifacts/bootstrap'),(Join-Path $root 'native-artifacts/evidence') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $native 'build/product/Release/Compositor.exe') -Destination (Join-Path $stage 'payload/Compositor.exe') -Force
$policy=Get-Content (Join-Path $stage 'payload/release-policy.json') -Raw | ConvertFrom-Json;$policy.version=$Version;$policy | ConvertTo-Json | Set-Content (Join-Path $stage 'payload/release-policy.json') -Encoding utf8
$report=Join-Path $stage 'evidence/compiled-version'
$proc=Start-Process (Join-Path $stage 'payload/Compositor.exe') -ArgumentList @('--write-version',('"'+$report+'"')) -WindowStyle Hidden -PassThru
if(!$proc.WaitForExit(15000) -or $proc.ExitCode -ne 0 -or (Get-Content $report -Raw) -ne $Version){throw '编译版本与发布版本不一致'}
