#requires -PSEdition Desktop
param([string]$SourceRoot,[ValidateSet('manual','stable-beta-off','beta-off','scheduled','blocked','parent-timeout','start-failure')][string]$Case='manual')
$ErrorActionPreference='Stop'
$fixtureName='ProgressPeek-lifecycle-'+[Guid]::NewGuid().ToString('N')
$fixtureRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $fixtureName
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
$rsa.PersistKeyInCsp=$false
$provider=New-Object Microsoft.CSharp.CSharpCodeProvider
function Compile-Fixture([string[]]$Sources,[string]$Destination) {
 $cp=New-Object CodeDom.Compiler.CompilerParameters
 $cp.GenerateExecutable=$true
 $cp.OutputAssembly=$Destination
 [void]$cp.ReferencedAssemblies.Add('System.dll')
 [void]$cp.ReferencedAssemblies.Add('System.Core.dll')
 [void]$cp.ReferencedAssemblies.Add('System.Web.Extensions.dll')
 $result=$provider.CompileAssemblyFromSource($cp,$Sources)
 if($result.Errors.HasErrors){throw ($result.Errors | Out-String)}
}
try {
 $candidate=Join-Path $fixtureRoot 'candidate-input.exe'
 $replacement='using System;using System.IO;using System.Reflection;using System.Threading;class Entry{static int Main(){string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);bool created;using(var mutex=new Mutex(true,"Local\\FIXTURE_MUTEX",out created)){if(!created){File.WriteAllText(Path.Combine(root,"rejected.txt"),"already running");return 9;}File.WriteAllText(Path.Combine(root,"launched.txt"),"new-version-started");return 0;}}}'
 Compile-Fixture @($replacement.Replace('FIXTURE_MUTEX',$fixtureName+'Overlay')) $candidate
 if($Case -eq 'start-failure'){[IO.File]::WriteAllText($candidate,'signed fixture bytes that are not an executable')}
 $bytes=[IO.File]::ReadAllBytes($candidate)
 $hash=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
 $version=if($Case -eq 'beta-off'){'1.1.0-beta.1'}else{'1.1.0'}
 $channel=if($Case -eq 'beta-off'){'beta'}else{'stable'}
 $manifest='{"app":"ProgressPeek","repository":"fixture/publisher","version":"'+$version+'","channel":"'+$channel+'","filename":"ProgressPeek.exe","size":'+$bytes.Length+',"sha256":"'+$hash+'"}'
 $manifestBytes=[Text.Encoding]::UTF8.GetBytes($manifest)
 [IO.File]::WriteAllBytes((Join-Path $fixtureRoot 'manifest-input.json'),$manifestBytes)
 [IO.File]::WriteAllBytes((Join-Path $fixtureRoot 'signature-input.sig'),$rsa.SignData($manifestBytes,'SHA256'))
 $distribution=[IO.File]::ReadAllText((Join-Path $SourceRoot 'DistributionConfig.cs')).Replace('Repository=""','Repository="fixture/publisher"').Replace('UpdatePublicKeyXml=""','UpdatePublicKeyXml="'+$rsa.ToXmlString($false)+'"')
 if(!$distribution.Contains($rsa.ToXmlString($false))){throw 'Fixture publisher substitution failed'}
 # Only namespace-like isolation constants change; installer control flow stays unchanged.
 $installer=[IO.File]::ReadAllText((Join-Path $SourceRoot 'UpdateInstaller.cs')).Replace('"ProgressPeek"','"'+$fixtureName+'"').Replace('TaskProgressOverlay',$fixtureName+'Overlay').Replace('ProgressPeekUpdateReady-',$fixtureName+'Ready-')
 # At the real replacement-start boundary, retain any erroneously open mutex
 # across the replacement's App-style constructor so the race is deterministic.
 $installer=$installer.Replace('Process.Start(new ProcessStartInfo(pending.Target)','FixtureBoundary.Start(new ProcessStartInfo(pending.Target)')
 # Shorten only the origin deadline in the timeout case; same wait/failure path.
 if($Case -eq 'parent-timeout'){$installer=$installer.Replace('parent.WaitForExit(60000)','parent.WaitForExit(1200)')}
 $entry=@'
using System;using System.IO;using System.Diagnostics;using System.Threading;
namespace ProgressPeek{
public static class BetaDiagnostics{public const string Version="1.0.0";}
public sealed class Settings{public bool InstallUpdatesAutomatically=true,CheckUpdatesAutomatically=true,IncludePrereleases=false;}
public sealed class Storage{public readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FIXTURE_ROOT");public Settings Settings=new Settings();}
class FixtureBoundary{public static Process Start(ProcessStartInfo info){string root=new Storage().Folder;Mutex retained=null;try{try{retained=Mutex.OpenExisting("Local\\FIXTURE_MUTEX");File.WriteAllText(Path.Combine(root,"open-mutex-boundary.txt"),"helper handle still exists");}catch(WaitHandleCannotBeOpenedException){}File.WriteAllText(Path.Combine(root,"start-boundary.txt"),"observed");var child=Process.Start(info);if(!child.WaitForExit(10000))throw new Exception("Replacement startup deadline");return child;}finally{if(retained!=null)retained.Dispose();}}}
class FixtureEntry{static int Main(string[] args){if(UpdateInstaller.HandleHelper(args))return Environment.ExitCode;try{string root=new Storage().Folder;var candidate=new UpdateCandidate{ManifestBytes=File.ReadAllBytes(Path.Combine(root,"manifest-input.json")),Signature=File.ReadAllBytes(Path.Combine(root,"signature-input.sig"))};var stage=new UpdateStaging(root);string mode="FIXTURE_CASE";bool beta=mode=="stable-beta-off"||mode=="beta-off";bool scheduled=mode=="scheduled"||mode=="blocked";stage.Stage(candidate,File.ReadAllBytes(Path.Combine(root,"candidate-input.exe")),Process.GetCurrentProcess().MainModule.FileName,beta,scheduled); if(mode=="manual"){string first=stage.State.PendingId;stage.Stage(candidate,File.ReadAllBytes(Path.Combine(root,"candidate-input.exe")),Process.GetCurrentProcess().MainModule.FileName,beta,scheduled);if(stage.State.PendingId!=first)throw new Exception("Identical verified release was staged twice");}
 if(mode=="beta-off"){try{stage.Launch(true,new Settings());return 7;}catch(InvalidDataException){File.WriteAllText(Path.Combine(root,"beta-rejected.txt"),"rejected");}var refresh=typeof(UpdateStaging).GetMethod("RevalidateChannel");if(refresh==null)throw new Exception("Saved beta-off must invalidate an incompatible pending beta");if((bool)refresh.Invoke(stage,new object[]{false})||stage.State.PendingId!=""||stage.State.Automatic)return 10;return 0;}
 if(mode=="stable-beta-off"){var refresh=typeof(UpdateStaging).GetMethod("RevalidateChannel");if(refresh==null)throw new Exception("Saved beta-off must retain a compatible signed stable package");if(!(bool)refresh.Invoke(stage,new object[]{false})||stage.State.PendingId=="")return 11;}
 if(mode=="blocked"){if(UpdateInstaller.TryScheduledLaunch())return 8;File.WriteAllText(Path.Combine(root,"blocked.txt"),"scheduled declined");return 0;}
 bool launched=scheduled?UpdateInstaller.TryScheduledLaunch():stage.Launch(true,new Settings());if(mode=="parent-timeout")Thread.Sleep(2400);return launched?0:2;}catch(Exception e){File.WriteAllText(Path.Combine(new Storage().Folder,"fixture-error.txt"),e.ToString());return 3;}}}
}
'@
 $entry=$entry.Replace('FIXTURE_ROOT',$fixtureName)
 $entry=$entry.Replace('FIXTURE_MUTEX',$fixtureName+'Overlay').Replace('FIXTURE_CASE',$Case)
 $target=Join-Path $fixtureRoot 'ProgressPeek.exe'
 Compile-Fixture @($distribution,$installer,[IO.File]::ReadAllText((Join-Path $SourceRoot 'UpdateSecurity.cs')),[IO.File]::ReadAllText((Join-Path $SourceRoot 'UpdateNetwork.cs')),$entry) $target
 $originalHash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
 $blocker=$null
 if($Case -eq 'blocked'){$created=$false;$blocker=New-Object Threading.Mutex($true,('Local\'+$fixtureName+'Overlay'),[ref]$created);if(!$created){throw 'Isolated blocker already existed'}}
 try{$parent=Start-Process -FilePath $target -WindowStyle Hidden -PassThru
 if(!$parent.WaitForExit(20000)){throw 'Fixture origin did not exit normally'}
 if($parent.ExitCode -ne 0){throw ('Fixture origin failed: '+$parent.ExitCode+' '+(Get-Content -LiteralPath (Join-Path $fixtureRoot 'fixture-error.txt') -Raw -ErrorAction SilentlyContinue))}
 }finally{if($blocker){$blocker.ReleaseMutex();$blocker.Dispose()}}
 if($Case -eq 'beta-off' -or $Case -eq 'blocked' -or $Case -eq 'parent-timeout'){
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $originalHash){throw 'Rejected launch changed installed bytes'}
  if(Test-Path -LiteralPath ($target+'.previous')){throw 'Rejected launch replaced installed executable'}
  $state=Get-Content -LiteralPath (Join-Path $fixtureRoot 'updates/state.json') -Raw | ConvertFrom-Json
  if($Case -ne 'beta-off' -and !$state.PendingId){throw 'Declined launch lost pending package'}
  if($Case -eq 'beta-off' -and ($state.PendingId -or $state.Automatic)){throw 'Saved beta-off retained an incompatible scheduled beta package'}
  if($Case -eq 'parent-timeout' -and ($state.Automatic -or !$state.Status.Contains('could not'))){throw 'Origin timeout did not disarm and report failure'}
  if($Case -eq 'beta-off' -and !(Test-Path -LiteralPath (Join-Path $fixtureRoot 'beta-rejected.txt'))){throw 'Incompatible beta was not rejected'}
  if($Case -eq 'blocked' -and !(Test-Path -LiteralPath (Join-Path $fixtureRoot 'blocked.txt'))){throw 'Running instance gate was not exercised'}
  $pendingResult=if($Case -eq 'beta-off'){'incompatible pending package invalidated'}else{'pending identity retained'}
  Write-Output ('PASS lifecycle '+$Case+': no replacement, '+$pendingResult+', policy/failure verified.')
  return
 }
 if($Case -eq 'start-failure'){
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while(!(Test-Path -LiteralPath ($target+'.failed')) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
  if(!(Test-Path -LiteralPath ($target+'.failed'))){throw 'Failed replacement start did not retain failed bytes'}
  if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $originalHash){throw 'Failed replacement start did not restore original'}
  if((Get-FileHash -LiteralPath ($target+'.failed') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Failed replacement bytes mismatch'}
  Write-Output 'PASS lifecycle start-failure: real Process.Start failure restored original and retained signed failed candidate.'
  return
 }
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 while(!(Test-Path -LiteralPath (Join-Path $fixtureRoot 'launched.txt')) -and !(Test-Path -LiteralPath (Join-Path $fixtureRoot 'rejected.txt')) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
 if(Test-Path -LiteralPath (Join-Path $fixtureRoot 'rejected.txt')){throw 'Replacement rejected App-style startup because helper mutex handle remained open'}
 if(!(Test-Path -LiteralPath (Join-Path $fixtureRoot 'launched.txt'))){throw 'Replacement never launched'}
 if(Test-Path -LiteralPath (Join-Path $fixtureRoot 'open-mutex-boundary.txt')){throw 'Helper kept mutex handle open at replacement startup'}
 if(!(Test-Path -LiteralPath (Join-Path $fixtureRoot 'start-boundary.txt'))){throw 'Deterministic handoff boundary not observed'}
 if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Installed bytes mismatch'}
 if((Get-FileHash -LiteralPath ($target+'.previous') -Algorithm SHA256).Hash -ne $originalHash){throw 'Recovery binary mismatch'}
 $state=Get-Content -LiteralPath (Join-Path $fixtureRoot 'updates/state.json') -Raw | ConvertFrom-Json
 if($state.PendingId -ne '' -or $state.Automatic){throw 'Pending update not cleared'}
 Write-Output ('PASS lifecycle '+$Case+': real helper chain, deterministic disposed-handle boundary, App-style createdNew startup, installed/backup hashes and cleared pending state.')
} finally {
 $rsa.Dispose();$provider.Dispose()
 $resolved=[IO.Path]::GetFullPath($fixtureRoot)
 $expected=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $fixtureName
 if($resolved -ne $expected -or $fixtureName -notmatch '^ProgressPeek-lifecycle-[a-f0-9]{32}$'){throw 'Unsafe cleanup target'}
 Start-Sleep -Milliseconds 250
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
