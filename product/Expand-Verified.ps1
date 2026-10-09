param([Parameter(Mandatory)][string]$Archive,[Parameter(Mandatory)][string]$Feed)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$envelope=Get-Content -LiteralPath (Join-Path $Feed 'feed.json') -Raw | ConvertFrom-Json
# The caller has verified this envelope with the compiled production key.
$manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.payload)) | ConvertFrom-Json
$expected=[Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($item in $manifest.files){$expected.Add(('payload/'+$item.path),[long]$item.size)}
$archiveHandle=[IO.Compression.ZipFile]::OpenRead($Archive)
try {
 $entries=@($archiveHandle.Entries)
 foreach($entry in $entries){
  if(!$expected.ContainsKey($entry.FullName) -or $entry.Length -ne $expected[$entry.FullName]){throw "压缩包包含未签名或大小不符的文件：$($entry.FullName)"}
  $relative=$entry.FullName
  if($relative.Contains('\') -or $relative.Contains(':') -or @($relative.Split('/') | Where-Object {$_ -in @('','..','.')}).Count){throw '压缩包路径不安全'}
  $expected.Remove($entry.FullName) | Out-Null
 }
 if($expected.Count){throw '压缩包缺少签名文件'}
 $base=[IO.Path]::GetFullPath($Feed)+[IO.Path]::DirectorySeparatorChar
 foreach($entry in $entries){
  $destination=[IO.Path]::GetFullPath((Join-Path $Feed $entry.FullName))
  if(!$destination.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw '解压路径越界'}
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false)
 }
} finally {$archiveHandle.Dispose()}
