#requires -Version 5.1
#requires -PSEdition Desktop
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('ProgressPeek-release-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
$rsa.PersistKeyInCsp=$false
try {
 $privatePath=Join-Path $fixture 'private.xml'
 [IO.File]::WriteAllText($privatePath,$rsa.ToXmlString($true))
 $publicXml=$rsa.ToXmlString($false)
 $source='using System.Reflection; [assembly: AssemblyVersion("1.2.3.0")][assembly: AssemblyFileVersion("1.2.3.0")][assembly: AssemblyInformationalVersion("1.2.3-beta.1")] namespace ProgressPeek {public static class DistributionConfig {public const string Repository="fixture/publisher";public const string UpdatePublicKeyXml="'+$publicXml+'";} class Entry {static void Main(){}}}'
 $compiler=New-Object Microsoft.CSharp.CSharpCodeProvider
 $parameters=New-Object CodeDom.Compiler.CompilerParameters
 $parameters.GenerateExecutable=$true
 $parameters.OutputAssembly=Join-Path $fixture 'fixture.exe'
 $compiled=$compiler.CompileAssemblyFromSource($parameters,$source)
 if ($compiled.Errors.HasErrors) { throw 'Synthetic release fixture did not compile.' }
 $release=Join-Path $fixture 'release'
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'prepare-release.ps1') -ExecutablePath $parameters.OutputAssembly -Repository 'fixture/publisher' -PrivateKeyPath $privatePath -OutputDirectory $release
 if ($LASTEXITCODE -ne 0) {throw 'Local release preparation failed.'}
 if (@(Get-ChildItem -LiteralPath $release).Count -ne 3) { throw 'Release script emitted unexpected assets.' }
 $manifest=[IO.File]::ReadAllBytes((Join-Path $release 'ProgressPeek.update.json'))
 $signature=[IO.File]::ReadAllBytes((Join-Path $release 'ProgressPeek.update.sig'))
 if (!$rsa.VerifyData($manifest,'SHA256',$signature)) { throw 'Detached release signature is invalid.' }
 $data=[Text.Encoding]::UTF8.GetString($manifest) | ConvertFrom-Json
 if ($data.version -cne '1.2.3-beta.1' -or $data.channel -cne 'beta' -or $data.repository -cne 'fixture/publisher') { throw 'Release identity mismatch.' }
 if ($data.sha256 -cne (Get-FileHash -LiteralPath (Join-Path $release 'ProgressPeek.exe') -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Release bytes differ from manifest.' }
 $oldAction=$ErrorActionPreference;$ErrorActionPreference='Continue'
 $wrongOutput=& powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'prepare-release.ps1') -ExecutablePath $parameters.OutputAssembly -Repository 'wrong/publisher' -PrivateKeyPath $privatePath -OutputDirectory (Join-Path $fixture 'wrong') 2>&1
 $wrongExit=$LASTEXITCODE;$ErrorActionPreference=$oldAction
 if ($wrongExit -eq 0) { throw 'Publisher mismatch was not rejected.' }
 Write-Output 'PASS local release script: compiled identity, three assets, actual RSA verification, executable hash, mismatched publisher rejection.'
} finally {
 $rsa.Dispose()
 if ($compiler) {$compiler.Dispose()}
 $absolute=[IO.Path]::GetFullPath($fixture)
 if (!$absolute.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'ProgressPeek-release-tests-*') { throw 'Unsafe temporary cleanup target.' }
 Remove-Item -LiteralPath $absolute -Recurse -Force
}
