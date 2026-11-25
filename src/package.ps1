#requires -Version 5.1
#requires -PSEdition Desktop
param([Parameter(Mandatory=$true)][string]$ExecutablePath,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$CompilerPath)
$ErrorActionPreference='Stop'
$binary=[IO.Path]::GetFullPath($ExecutablePath)
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $destination){if(@(Get-ChildItem -LiteralPath $destination -Force).Count){throw 'Choose a new or empty package directory.'}}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($binary)
if($version.FileVersion -ne '0.4.0.0' -or $version.ProductVersion -ne '0.4.0-beta' -or [IO.Path]::GetFileName($binary) -ne 'ProgressPeek.exe'){throw 'Use the matching ProgressPeek beta binary.'}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-Zip([string]$Path,[string[]]$Names,[string]$Root){
 $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew);$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($name in ($Names|Sort-Object)){if($name -match '(^/|(^|/)\.\.(/|$)|:|\\)'){throw 'Unsafe allowlist entry'};$source=Join-Path $Root $name;if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing public source $name"};$entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal);$entry.LastWriteTime=[DateTimeOffset]'2026-10-01T00:00:00Z';$input=[IO.File]::OpenRead($source);$output=$entry.Open();try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}}}finally{$zip.Dispose();$stream.Dispose()}
}
$docs=@('README.md','PRIVACY.md','SECURITY.md','CONTRIBUTING.md','CHANGELOG.md','RELEASE.md','CLEAN-PC.md')
$portableRoot=Join-Path $destination 'portable-stage';New-Item -ItemType Directory -Path $portableRoot | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $portableRoot 'ProgressPeek.exe');foreach($doc in $docs){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $doc) -Destination $portableRoot}
Write-Zip (Join-Path $destination 'ProgressPeek-0.4.0-beta-portable.zip') (@('ProgressPeek.exe')+$docs) $portableRoot
# Exact committed allowlist: no recursive source collection, private state or generated executables.
$sourceNames=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-manifest.txt') | Where-Object {$_ -and !$_.StartsWith('#')}
Write-Zip (Join-Path $destination 'ProgressPeek-0.4.0-beta-source.zip') $sourceNames $PSScriptRoot
if($CompilerPath){& $CompilerPath ('/DBinaryPath='+$binary) ('/DBuildOutput='+$destination) (Join-Path $PSScriptRoot 'installer.iss');if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}}
$assets=Get-ChildItem -LiteralPath $destination -File | Where-Object Extension -in @('.zip','.exe') | Sort-Object Name
$checksumLines=@($assets|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name})
[IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'),$checksumLines,(New-Object Text.UTF8Encoding($false)))
# Only our exact staging files are removed; unrelated paths are never traversed.
foreach($name in (@('ProgressPeek.exe')+$docs)){Remove-Item -LiteralPath (Join-Path $portableRoot $name)}
Remove-Item -LiteralPath $portableRoot
Write-Output 'Prepared local portable/source ZIPs, checksums and optional installer. Nothing published. License/publisher gates remain pending.'
