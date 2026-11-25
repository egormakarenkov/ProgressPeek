#requires -PSEdition Desktop
param([Parameter(Mandatory=$true)][string]$ExecutablePath,[Parameter(Mandatory=$true)][string]$CompilerPath)
$ErrorActionPreference='Stop'
$token=[Guid]::NewGuid().ToString('N');$id='PPF'+$token
$root=Join-Path ([IO.Path]::GetTempPath()) ('ProgressPeek-installer-fixture-'+$token)
$app=Join-Path $root 'app';$data=Join-Path $root 'data';$release=Join-Path $root 'release'
$subKeyPath='Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$id+'_is1'
$registration='HKCU:\'+$subKeyPath
New-Item -ItemType Directory -Path $app,$data,$release | Out-Null
[IO.File]::WriteAllText((Join-Path $app 'unrelated.txt'),'keep unrelated')
[IO.File]::WriteAllText((Join-Path $data 'settings-sentinel.txt'),'keep data')

function Test-RegistryExists([string]$SubKey){
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if ($null -ne $key){
        $key.Close()
        return $true
    }
    return $false
}

function Run-Installer([string]$File,[string]$Arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=99'){
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw 'Disposable installer fixture failed'}
}

function Wait-Until([scriptblock]$Condition,[int]$TimeoutSeconds=15){
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while($sw.ElapsedMilliseconds -lt ($TimeoutSeconds * 1000)){
        if(& $Condition){return $true}
        Start-Sleep -Milliseconds 100
    }
    return (& $Condition)
}

try{
 if(Test-RegistryExists $subKeyPath){throw 'Fixture AppId is already registered'}
 & $CompilerPath ('/DBinaryPath='+[IO.Path]::GetFullPath($ExecutablePath)) ('/DBuildOutput='+$release) ('/DFixtureId='+$id) ('/DFixtureDir='+$app) (Join-Path $PSScriptRoot 'installer.iss')
 if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed'}
 $setup=Join-Path $release 'ProgressPeek-0.4.0-beta-setup.exe';Run-Installer $setup

 if(! (Wait-Until {Test-RegistryExists $subKeyPath} 5)){throw 'Fixture registry registration was not created'}
 $installedBinary=Join-Path $app 'ProgressPeek.exe'
 if(!(Test-Path -LiteralPath $installedBinary) -or (Get-FileHash -LiteralPath $installedBinary).Hash -ne (Get-FileHash -LiteralPath $ExecutablePath).Hash){throw 'Isolated install/hash failed'}

 Run-Installer $setup
 if(!(Test-Path -LiteralPath (Join-Path $app 'unrelated.txt'))){throw 'Upgrade erased unrelated file'}

 Run-Installer (Join-Path $app 'unins000.exe')
 if(! (Wait-Until {!(Test-RegistryExists $subKeyPath)} 15)){throw 'Fixture uninstall registration remains'}
 if(! (Wait-Until {!(Test-Path -LiteralPath $installedBinary)} 15)){throw 'Fixture installed executable remains'}

 if((Get-Content -LiteralPath (Join-Path $app 'unrelated.txt') -Raw) -ne 'keep unrelated' -or (Get-Content -LiteralPath (Join-Path $data 'settings-sentinel.txt') -Raw) -ne 'keep data'){throw 'Uninstall erased fixture sentinels'}
 Write-Output 'PASS disposable per-user install, repeat-install upgrade, uninstall, unique registration, unrelated/data sentinels; candidate never launched.'
}finally{
 if(Test-RegistryExists $subKeyPath){throw ('Fixture registration needs explicit cleanup: '+$registration)}
 if([IO.Path]::GetFileName($root) -notmatch '^ProgressPeek-installer-fixture-[a-f0-9]{32}$' -or [IO.Path]::GetDirectoryName($root).TrimEnd('\') -ne [IO.Path]::GetTempPath().TrimEnd('\')){throw 'Unsafe cleanup'}
 Remove-Item -LiteralPath $root -Recurse -Force
}
