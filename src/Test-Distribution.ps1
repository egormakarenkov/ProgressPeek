#requires -PSEdition Desktop
param([Parameter(Mandatory=$true)][string]$ExecutablePath)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('ProgressPeek-packages-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try{
 $first=Join-Path $fixture 'first';$second=Join-Path $fixture 'second'
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'package.ps1') -ExecutablePath $ExecutablePath -OutputDirectory $first
 if($LASTEXITCODE -ne 0){throw 'Public package generation failed'}
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'package.ps1') -ExecutablePath $ExecutablePath -OutputDirectory $second
 if($LASTEXITCODE -ne 0){throw 'Repeated package generation failed'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 foreach($name in @('ProgressPeek-0.4.0-beta-portable.zip','ProgressPeek-0.4.0-beta-source.zip')){
  if((Get-FileHash -LiteralPath (Join-Path $first $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $second $name)).Hash){throw 'ZIP is not deterministic'}
  $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $first $name));try{$entries=@($zip.Entries|ForEach-Object FullName);if($entries -match '(^/|(^|/)\.\.(/|$)|:|\\|(?i)(auth|checks\.json|settings\.json|last-check|\.pem|key\.xml|\.previous|\.failed))'){throw 'Unsafe/private archive member'};if($name -like '*source*'){$allowed=@(Get-Content (Join-Path $PSScriptRoot 'source-manifest.txt')|Where-Object{$_ -and !$_.StartsWith('#')});if(@(Compare-Object ($entries|Sort-Object) ($allowed|Sort-Object)).Count){throw 'Source ZIP differs from allowlist'};if($entries -match '\.exe$'){throw 'Generated executable in source ZIP'}}else{if($entries.Count -ne 8 -or $entries -notcontains 'ProgressPeek.exe'){throw 'Portable ZIP is not minimal executable and public docs'}}}finally{$zip.Dispose()}
 }
 Write-Output 'PASS deterministic source/portable allowlists and private/generated-state exclusion.'
}finally{if([IO.Path]::GetFileName($fixture) -notmatch '^ProgressPeek-packages-[a-f0-9]{32}$' -or [IO.Path]::GetDirectoryName($fixture).TrimEnd('\') -ne [IO.Path]::GetTempPath().TrimEnd('\')){throw 'Unsafe cleanup'};Remove-Item -LiteralPath $fixture -Recurse -Force}
