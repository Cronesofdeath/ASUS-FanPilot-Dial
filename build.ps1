param([switch]$Test, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'bin\app' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$references = @('System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Xaml.dll') | ForEach-Object { "/r:$_" }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') | ForEach-Object { "/r:$wpf\$_" }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object FullName)
$resources = @(
    "/resource:$PSScriptRoot\assets\FanPilot.ico,FanPilot.ico",
    "/resource:$PSScriptRoot\src\UI\Shell.xaml,Shell.xaml",
    "/resource:$PSScriptRoot\src\UI\Settings.xaml,Settings.xaml",
    "/resource:$PSScriptRoot\src\UI\Curves.xaml,Curves.xaml",
    "/resource:$PSScriptRoot\src\UI\Theme.xaml,Theme.xaml"
)
& "$framework\csc.exe" /nologo /optimize+ /warn:4 /warnaserror+ /platform:x64 /target:winexe /main:FanPilot.Program "/out:$output\ASUS FanPilot Dial.exe" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\assets\FanPilot.ico" @references @resources @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Write-Output "Built $output\ASUS FanPilot Dial.exe"
if ($Test) { & (Join-Path $PSScriptRoot 'test.ps1') -AppDirectory $output -SkipBuild }
