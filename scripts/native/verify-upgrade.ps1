param([Parameter(Mandatory)][string]$Bootstrap,[Parameter(Mandatory)][string]$CandidateFeed,[Parameter(Mandatory)][string]$Output,[string]$PreviousUrl)
$ErrorActionPreference='Stop'
$sandbox=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $sandbox){throw '升级验收只允许全新的隔离目录'}
New-Item -ItemType Directory -Path $sandbox | Out-Null
Get-ChildItem -LiteralPath $Bootstrap | Copy-Item -Destination $sandbox -Recurse
$exe=Join-Path $sandbox 'CompositorStart.exe'
function Invoke-Gate([string[]]$Arguments,[int]$Expected=0){
 $proc=Start-Process $exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $sandbox 'last-error.log')
 if(!$proc.WaitForExit(300000)){Stop-Process -Id $proc.Id;throw '安装升级验收超时'}
 if($proc.ExitCode -ne $Expected){throw "更新门退出码错误：$($proc.ExitCode)，预期 $Expected；$(Get-Content (Join-Path $sandbox 'last-error.log') -Raw)"}
}
function State {return (Get-Content (Join-Path $sandbox 'state/active.json') -Raw | ConvertFrom-Json)}
$checks=@()
if($PreviousUrl){
 Invoke-Gate @('--initialize','--install','--quiet','--url',('"'+$PreviousUrl+'"'))
 $before=(State).current;$documents=Join-Path $sandbox '中文项目保留门'
 Invoke-Gate @('--quiet','--wait','--','--product-gates','--evidence',('"'+$documents+'"'))
 $original=Join-Path $documents 'Project 実証 test.comp';$project=Join-Path $documents '中文升级验收.comp';Move-Item -LiteralPath $original -Destination $project
 $hashes=@(Get-ChildItem -LiteralPath $project -Recurse -File | ForEach-Object {([IO.Path]::GetRelativePath($project,$_.FullName))+':'+(Get-FileHash -LiteralPath $_.FullName).Hash})
 $checks+='真实 GitHub 上一版本安装及中文项目保存'
 $bad=Join-Path $sandbox 'bad-feed';New-Item -ItemType Directory -Path $bad | Out-Null
 $envelope=Get-Content (Join-Path $CandidateFeed 'feed.json') -Raw | ConvertFrom-Json
 $signature=[Convert]::FromBase64String($envelope.signature);$signature[0]=$signature[0] -bxor 1;$envelope.signature=[Convert]::ToBase64String($signature)
 $envelope | ConvertTo-Json -Compress | Set-Content (Join-Path $bad 'feed.json') -Encoding utf8
 Invoke-Gate @('--quiet','--verify-feed',('"'+(Join-Path $bad 'feed.json')+'"')) 2
 if((State).current -ne $before){throw '坏签名改变了当前版本'};$checks+='坏签名拒绝'
 $badPayload=Join-Path $sandbox 'bad-payload';New-Item -ItemType Directory -Path (Join-Path $badPayload 'payload') -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $CandidateFeed 'feed.json') -Destination $badPayload
 [IO.File]::WriteAllBytes((Join-Path $badPayload 'payload/Compositor.exe'),[byte[]]@(0))
 Invoke-Gate @('--install','--quiet','--feed',('"'+$badPayload+'"')) 2
 if((State).current -ne $before -or (State).pending -or (Get-Content (Join-Path $sandbox 'last-error.log') -Raw) -notmatch 'hash/signature rejected'){throw '损坏载荷没有按哈希错误拒绝'};$checks+='坏包拒绝且不激活'
 Invoke-Gate @('--install','--quiet','--feed',('"'+$CandidateFeed+'"'),'--gate-fail-finalize') 2
 if((State).current -ne $before -or (State).pending){throw '失败激活没有回退旧版'};$checks+='健康检查后失败回退'
 Invoke-Gate @('--quiet','--wait','--','--update-health-check');$checks+='回退旧版仍能运行'
}
Invoke-Gate @('--initialize','--install','--quiet','--feed',('"'+$CandidateFeed+'"'))
$expectedEnvelope=Get-Content (Join-Path $CandidateFeed 'feed.json') -Raw | ConvertFrom-Json
$manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($expectedEnvelope.payload)) | ConvertFrom-Json
if((State).current -ne $manifest.version -or (State).pending){throw '候选版本没有健康激活'}
$versionFile=Join-Path $sandbox 'running-version'
Invoke-Gate @('--quiet','--wait','--','--write-version',('"'+$versionFile+'"'))
if((Get-Content $versionFile -Raw) -ne $manifest.version){throw '重启实际运行的版本不匹配'};$checks+='健康安装并重启目标版本'
if($PreviousUrl){
 $after=@(Get-ChildItem -LiteralPath $project -Recurse -File | ForEach-Object {([IO.Path]::GetRelativePath($project,$_.FullName))+':'+(Get-FileHash -LiteralPath $_.FullName).Hash})
 if(Compare-Object $hashes $after){throw '升级改变了中文项目文件'}
 $render=Join-Path $sandbox '升级后导出.png';Invoke-Gate @('--quiet','--wait','--','--render-project',('"'+$project+'"'),'--output',('"'+$render+'"'))
 if(!(Test-Path $render)){throw '升级后中文项目无法重开导出'};$checks+='中文项目逐文件保持并重新导出'
}
[ordered]@{status='passed';version=$manifest.version;checks=$checks} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $sandbox 'upgrade-gates.json') -Encoding utf8
