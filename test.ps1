param([switch]$SkipBuild, [string]$AppDirectory, [switch]$SkipUi, [ValidateSet('Unit','Integration','Ui')][string[]]$Suites = @('Unit','Integration','Ui'))
$ErrorActionPreference = 'Stop'
$app = if ($AppDirectory) { [IO.Path]::GetFullPath($AppDirectory) } else { Join-Path $PSScriptRoot 'bin\app' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $app }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$references = @('System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Xaml.dll') | ForEach-Object { "/r:$_" }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') | ForEach-Object { "/r:$wpf\$_" }
$references += "/r:$app\ASUS FanPilot Dial.exe"
$env:FANPILOT_TEST_FOLDER = $app
$env:FANPILOT_TEST_OUTPUT = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $env:FANPILOT_TEST_OUTPUT -Force | Out-Null
foreach ($suite in $Suites) {
    if ($SkipUi -and $suite -eq 'Ui') { continue }
    $name = "FanPilot.$($suite)Tests"
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "tests\$suite") -Filter '*.cs' | Sort-Object FullName | ForEach-Object FullName)
    & "$framework\csc.exe" /nologo /warn:4 /warnaserror+ /platform:x64 /target:exe "/out:$app\$name.exe" @references @sources
    if ($LASTEXITCODE -ne 0) { throw "$suite test compilation failed." }
    if ($suite -eq 'Ui') {
        $folder = Join-Path $env:FANPILOT_TEST_OUTPUT 'ui'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $resultPath = Join-Path $folder 'result.txt'
        if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
        $process = Start-Process -FilePath "$app\$name.exe" -ArgumentList "`"$folder`"" -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(45000)) { throw 'UI tests timed out.' }
        $result = Get-Content -LiteralPath $resultPath -Raw
        Write-Output $result
        if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS:')) { throw 'UI tests failed.' }
    } else {
        & "$app\$name.exe"
        if ($LASTEXITCODE -ne 0) { throw "$suite tests failed." }
    }
}
