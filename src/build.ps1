param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'outputs' }
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
& $compiler /nologo /target:exe /out:"$taskRoot\Tests.exe" /r:System.Web.Extensions.dll "$taskRoot\Core.cs" "$taskRoot\Storage.cs" "$taskRoot\Setup.cs" "$taskRoot\CliEstimator.cs" "$taskRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Tests compilation failed' }
& "$taskRoot\Tests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
& $compiler /nologo /target:exe /main:UsageTests /out:"$taskRoot\UsageTests.exe" /r:System.Web.Extensions.dll "$taskRoot\Core.cs" "$taskRoot\Storage.cs" "$taskRoot\Setup.cs" "$taskRoot\CliEstimator.cs" "$taskRoot\Usage.cs" "$taskRoot\UsageTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Usage tests compilation failed' }
& "$taskRoot\UsageTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Usage tests failed' }
$sources = Get-ChildItem -LiteralPath $taskRoot -Filter '*.cs' | Where-Object Name -notlike '*Tests.cs' | ForEach-Object FullName
$regressionSources = Get-ChildItem -LiteralPath $taskRoot -Filter '*Tests.cs' | Where-Object Name -notin @('Tests.cs','UsageTests.cs') | ForEach-Object FullName
& $compiler /nologo /target:exe /main:RegressionTests /out:"$taskRoot\RegressionTests.exe" $refs $sources $regressionSources
if ($LASTEXITCODE -ne 0) { throw 'Regression tests compilation failed' }
& "$taskRoot\RegressionTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
& powershell.exe -NoProfile -File "$taskRoot\UpdateReleaseTests.ps1"
if ($LASTEXITCODE -ne 0) { throw 'Local release preparation tests failed' }
& $compiler /nologo /target:winexe /platform:anycpu /out:"$taskOutput\ProgressPeek.exe" /win32manifest:"$taskRoot\app.manifest" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
Write-Output "Built $taskOutput\ProgressPeek.exe"
