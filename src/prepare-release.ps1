#requires -Version 5.1
#requires -PSEdition Desktop
param(
 [Parameter(Mandatory=$true)][string]$ExecutablePath,
 [Parameter(Mandatory=$true)][string]$Repository,
 [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$binary=[IO.Path]::GetFullPath($ExecutablePath)
$privateKey=[IO.Path]::GetFullPath($PrivateKeyPath)
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if ($Repository -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?/[A-Za-z0-9_.-]{1,100}$' -or $Repository.Split('/')[0].Contains('--') -or $Repository.Split('/')[1] -in @('.','..')) { throw 'Use an exact owner/repository identity.' }
if (!(Test-Path -LiteralPath $binary -PathType Leaf) -or !(Test-Path -LiteralPath $privateKey -PathType Leaf)) { throw 'Executable and externally supplied RSA private key are required.' }
if ($privateKey.StartsWith($PSScriptRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $privateKey.StartsWith($destination+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'The private key must stay outside the source and release directories.' }
if (Test-Path -LiteralPath $destination) { if (@(Get-ChildItem -LiteralPath $destination -Force).Count) { throw 'Choose a new or empty release output directory.' } }
$assembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom($binary)
$attributes=[Reflection.CustomAttributeData]::GetCustomAttributes($assembly)
$info=$attributes | Where-Object {$_.AttributeType.FullName -eq 'System.Reflection.AssemblyInformationalVersionAttribute'}
$version=[string]$info.ConstructorArguments[0].Value
if ($version -notmatch '^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-beta(?:\.(0|[1-9][0-9]{0,8}))?)?$') { throw 'The binary must have a supported informational version.' }
$numeric=$version.Split('-')[0]
if ($assembly.GetName().Version.ToString() -ne ($numeric+'.0')) { throw 'Assembly and informational versions differ.' }
$fileVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion
if ($fileVersion -ne ($numeric+'.0')) { throw 'File and informational versions differ.' }
$channel=if ($version.Contains('-beta')) {'beta'} else {'stable'}
# Read the publisher constants without executing the candidate assembly.
$config=$assembly.GetType('ProgressPeek.DistributionConfig',$true)
if ([string]$config.GetField('Repository').GetRawConstantValue() -cne $Repository) { throw 'Binary repository differs from the requested publisher identity. Configure and rebuild first.' }
$publicXml=[string]$config.GetField('UpdatePublicKeyXml').GetRawConstantValue()
if (!$publicXml) { throw 'Binary has no compiled publisher public key. Configure and rebuild first.' }
$csp=New-Object Security.Cryptography.CspParameters
$csp.ProviderType=24
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider($csp)
$rsa.PersistKeyInCsp=$false
$public=New-Object Security.Cryptography.RSACryptoServiceProvider($csp)
$public.PersistKeyInCsp=$false
try {
 $rsa.FromXmlString([IO.File]::ReadAllText($privateKey))
 $public.FromXmlString($publicXml)
 if ($rsa.KeySize -lt 2048 -or $rsa.PublicOnly -or $rsa.ToXmlString($false) -cne $public.ToXmlString($false)) { throw 'The external private key must match the compiled publisher public key and use at least 2048 bits.' }
 $size=(Get-Item -LiteralPath $binary).Length
 if ($size -lt 1 -or $size -gt 33554432) { throw 'Executable exceeds the updater package limit.' }
 $hash=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
 $manifest='{"app":"ProgressPeek","repository":"'+$Repository+'","version":"'+$version+'","channel":"'+$channel+'","filename":"ProgressPeek.exe","size":'+$size.ToString([Globalization.CultureInfo]::InvariantCulture)+',"sha256":"'+$hash+'"}'
 $bytes=(New-Object Text.UTF8Encoding($false,$true)).GetBytes($manifest)
 $signature=$rsa.SignData($bytes,'SHA256')
 if (!$public.VerifyData($bytes,'SHA256',$signature)) { throw 'Local signature verification failed.' }
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 Copy-Item -LiteralPath $binary -Destination (Join-Path $destination 'ProgressPeek.exe')
 if ((Get-FileHash -LiteralPath (Join-Path $destination 'ProgressPeek.exe') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw 'Copied executable differs from the signed binary.' }
 [IO.File]::WriteAllBytes((Join-Path $destination 'ProgressPeek.update.json'),$bytes)
 [IO.File]::WriteAllBytes((Join-Path $destination 'ProgressPeek.update.sig'),$signature)
 Write-Output ('Prepared three local assets for tag v'+$version+' ('+$channel+'). Nothing was uploaded.')
} finally { $rsa.Dispose();$public.Dispose() }
