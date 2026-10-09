param([string]$Version='0.2.0',[string]$Output='native-artifacts',[switch]$SkipDependencies)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$native=Join-Path $root 'windows-native'
$outputPath=[IO.Path]::GetFullPath((Join-Path $root $Output))
function Run { & $args[0] @($args | Select-Object -Skip 1); if($LASTEXITCODE){throw "命令失败：$($args[0])，退出码 $LASTEXITCODE"} }
function Fetch([string]$Url,[string]$Path,[string]$Hash){
 New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
 if(!(Test-Path -LiteralPath $Path)){Invoke-WebRequest $Url -OutFile $Path}
 if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Hash){throw "依赖摘要不符：$Path"}
}
New-Item -ItemType Directory -Force $outputPath | Out-Null
Run python (Join-Path $PSScriptRoot 'verify-source.py')
$vs=& 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
if(!$vs){throw '需要 Visual Studio 2022 C++ 工具链；不会自动进行管理员安装。'}
& (Join-Path $vs 'Common7/Tools/Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$cmake=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$qtLock=Get-Content (Join-Path $native 'dependencies.lock.json') -Raw | ConvertFrom-Json
$qt=Join-Path $native 'dependencies/qt';$downloads=Join-Path $native 'dependencies/downloads'
$reference=Join-Path $downloads 'reference.zip'
if(!$SkipDependencies){
 Fetch 'https://github.com/IAmTheBlurr/CompositorWindows/releases/download/v0.1.4-preview/CompositorWindows-0.1.4-portable.zip' $reference '50af223bfab4d02294874d014b6431b83b3ee0f4b92dfaf3914ae9a6e67e170b'
 if(!(Test-Path (Join-Path $downloads 'reference/CompositorWindows/Compositor.exe'))){Expand-Archive -LiteralPath $reference -DestinationPath (Join-Path $downloads 'reference')}
 Fetch $qtLock.qt.url (Join-Path $downloads 'qtbase.7z') $qtLock.qt.sha256
 if(!(Test-Path (Join-Path $qt 'lib/cmake/Qt6/Qt6Config.cmake'))){New-Item -ItemType Directory -Force $qt | Out-Null;Push-Location $qt;try{Run $cmake -E tar xf (Join-Path $downloads 'qtbase.7z')}finally{Pop-Location}}
 & (Join-Path $native 'scripts/bootstrap-imaging.ps1') -VsRoot $vs -SkipModel
 if($LASTEXITCODE){throw '编解码依赖构建失败'}
 $imaging=Join-Path $native 'dependencies/imaging'
 $lock=Get-Content (Join-Path $imaging 'lock.json') -Raw | ConvertFrom-Json
 $ort=@($lock.assets | Where-Object {$_.path.EndsWith('onnxruntime-win-x64-1.30.0.zip')})[0]
 Fetch $ort.url (Join-Path $native $ort.path) $ort.sha256
 Expand-Archive -LiteralPath (Join-Path $native $ort.path) -DestinationPath $imaging -Force
 New-Item -ItemType Directory -Force (Join-Path $imaging 'model') | Out-Null
 Copy-Item -LiteralPath (Join-Path $downloads 'reference/CompositorWindows/models/birefnet-lite.onnx') -Destination (Join-Path $imaging 'model/birefnet-lite.onnx')
 & (Join-Path $native 'scripts/restore-imaging-runtime.ps1') -RuntimeDirectory (Join-Path $downloads 'reference/CompositorWindows')
 if((Get-FileHash (Join-Path $imaging 'model/birefnet-lite.onnx')).Hash -ine $lock.model.onnx_sha256){throw '离线模型摘要不符'}
}
$env:PATH="$qt/bin;$env:PATH";$env:QT_PLUGIN_PATH=Join-Path $qt 'plugins'
# Make restored MSBuild dependency timestamps reflect source commits, not the
# fresh checkout time. A source change still advances its dependency timestamp.
$nativeTime=[DateTimeOffset]::FromUnixTimeSeconds([long](& git -C $root log -1 --format=%ct -- windows-native/src windows-native/assets)).UtcDateTime
foreach($path in @(& git -C $root ls-files windows-native/src windows-native/assets)){
 if(Test-Path -LiteralPath (Join-Path $root $path)){(Get-Item -LiteralPath (Join-Path $root $path)).LastWriteTimeUtc=$nativeTime}
}
foreach($path in @(& git -C $root ls-files product)){
 $time=[DateTimeOffset]::FromUnixTimeSeconds([long](& git -C $root log -1 --format=%ct -- $path)).UtcDateTime
 (Get-Item -LiteralPath (Join-Path $root $path)).LastWriteTimeUtc=$time
}
Run $cmake -S $native -B (Join-Path $native 'build/product') -G 'Visual Studio 17 2022' -A x64 "-DCMAKE_PREFIX_PATH=$qt" -DBUILD_TESTING=ON -DCOMPOSITOR_PRODUCT=ON "-DPRODUCT_VERSION=$Version"
$build=Join-Path $native 'build/product';$release=Join-Path $build 'Release'
Run $cmake --build $build --config Release --target CompositorProduct CompositorStart core_tests persistence_tests command_ui_tests window_chrome_tests --parallel 4
Push-Location $build
try {Run ctest -C Release --no-tests=error --output-on-failure -R '^(core\.|persistence\.|command_ui\.)' -E '^persistence\.process_termination$'}finally{Pop-Location}
# The original chrome witness is an executable workflow, not a registered CTest.
Run (Join-Path $release 'window_chrome_tests.exe') (Join-Path $outputPath 'chrome-evidence')
$payload=Join-Path $outputPath 'payload';New-Item -ItemType Directory -Force $payload | Out-Null
Get-ChildItem -LiteralPath (Join-Path $downloads 'reference/CompositorWindows') | Copy-Item -Destination $payload -Recurse
Copy-Item -LiteralPath (Join-Path $release 'Compositor.exe') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $root 'product/Expand-Verified.ps1') -Destination $payload
Run (Join-Path $qt 'bin/windeployqt.exe') --release --no-translations --no-opengl-sw --no-compiler-runtime --dir $payload (Join-Path $payload 'Compositor.exe')
$bootstrap=Join-Path $outputPath 'bootstrap';New-Item -ItemType Directory -Force $bootstrap | Out-Null
Copy-Item -LiteralPath (Join-Path $release 'CompositorStart.exe') -Destination $bootstrap
Copy-Item -LiteralPath (Join-Path $root 'product/Expand-Verified.ps1') -Destination $bootstrap
Run (Join-Path $qt 'bin/windeployqt.exe') --release --no-translations --no-opengl-sw --no-compiler-runtime --dir $bootstrap (Join-Path $bootstrap 'CompositorStart.exe')
$crt=Join-Path $env:VCToolsRedistDir 'x64/Microsoft.VC143.CRT'
Get-ChildItem $crt -Filter '*.dll' | Copy-Item -Destination $bootstrap
Get-ChildItem $crt -Filter '*.dll' | Copy-Item -Destination $payload -Force
$sourceArchive=Join-Path $payload 'sources/Compositor-sun-dove-source.zip'
Run git -C $root archive --format=zip "--output=$sourceArchive" HEAD
Copy-Item -LiteralPath (Join-Path $root 'docs/windows/使用说明.md') -Destination (Join-Path $payload '使用说明.md')
$state=Get-Content (Join-Path $root 'product/upstream-state.json') -Raw | ConvertFrom-Json
$policy=[ordered]@{schema=1;channel='stable';automaticUpdates=$true;version=$Version;macUpstreamCommit=$state.macUpstreamCommit;windowsUpstreamCommit=$state.windowsUpstreamCommit;windowsMacBaseline=$qtLock.upstream}
$policy | ConvertTo-Json | Set-Content (Join-Path $payload 'release-policy.json') -Encoding utf8
[IO.File]::WriteAllText((Join-Path $payload 'README.txt'),"Compositor Windows 中文版 $Version`n请通过安装目录根部 CompositorStart.exe 启动；应用启动会检查本仓库签名更新。`n原作者 Robbie Tilton；Windows 移植 IAmTheBlurr；中文与自动更新 sun-dove。`n保留原完整界面、图标与 Inter 字体。中文缺失字形由 Windows 系统字体补齐。`n请将 .comp 项目保存在应用目录之外。完整最新 Mac 功能尚未迁移。`n许可证与可重建源代码位于 licenses / sources。安装包没有 Authenticode 签名。`n")
$evidence=Join-Path $outputPath 'evidence';New-Item -ItemType Directory -Force $evidence | Out-Null
$proc=Start-Process (Join-Path $payload 'Compositor.exe') -ArgumentList @('--product-gates','--warp','--evidence',('"'+$evidence+'"')) -PassThru
if(!$proc.WaitForExit(180000)){Stop-Process -Id $proc.Id;throw '实际原界面中文体验门超时'}
if($proc.ExitCode -ne 0 -or !(Test-Path (Join-Path $evidence 'product-gates.json'))){throw '实际原界面中文体验门失败'}
Write-Output "原界面中文构建与验收通过：$outputPath"
