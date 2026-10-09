param(
    [string]$Version = '0.1.0',
    [string]$Output,
    [switch]$Package,
    [string]$NuGetConfig,
    [string]$Shortcuts = 'Desktop,StartMenuRoot'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$Output) { $Output = Join-Path $repoRoot "windows/artifacts/$Version" }
$Output = [IO.Path]::GetFullPath($Output)
$config = if ($NuGetConfig) { $NuGetConfig } else { Join-Path $repoRoot 'windows/NuGet.Config' }
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw ('dotnet 命令失败，退出码 '+$LASTEXITCODE) } }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
Invoke-Dotnet restore (Join-Path $repoRoot 'windows/Gates/Compositor.Gates.csproj') --configfile $config --locked-mode
Invoke-Dotnet run --project (Join-Path $repoRoot 'windows/Gates/Compositor.Gates.csproj') --no-restore --configuration Release
Invoke-Dotnet publish (Join-Path $repoRoot 'windows/App/Compositor.Windows.csproj') --configuration Release --runtime win-x64 --self-contained true --output (Join-Path $Output 'app') "-p:Version=$Version" "-p:RestoreConfigFile=$config" '-p:RestoreLockedMode=true'
$uiFolder = Join-Path $Output 'ui'
$exe = Join-Path $Output 'app/Compositor.Windows.exe'
$process = Start-Process -FilePath $exe -ArgumentList @('--ui-gates', ('"'+$uiFolder+'"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw '中文体验门超时' }
$reportPath = Join-Path $uiFolder 'ui-gates.json'
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $reportPath) -or (Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json).failed -ne 0) { throw '中文体验门失败' }
if ($Package) {
    Invoke-Dotnet tool restore --tool-manifest (Join-Path $repoRoot 'windows/.config/dotnet-tools.json') --configfile $config
    Push-Location (Join-Path $repoRoot 'windows')
    try { Invoke-Dotnet tool run vpk -- pack --packId CompositorWindows --packVersion $Version --packDir (Join-Path $Output 'app') --mainExe Compositor.Windows.exe --channel win --runtime win-x64 --packTitle 'Compositor Windows 中文版' --packAuthors sun-dove --delta None --shortcuts $Shortcuts --icon (Join-Path $repoRoot 'windows/resources/app.ico') --outputDir (Join-Path $Output 'release') }
    finally { Pop-Location }
}
Write-Output "Windows 构建与验收完成：$Output"
