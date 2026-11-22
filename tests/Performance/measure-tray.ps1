param(
    [Parameter(Mandatory)][string]$BaselineExe,
    [Parameter(Mandatory)][string]$CandidateExe
)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.Core.dll','System.Runtime.Serialization.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll') |
    ForEach-Object { "/r:$_" }
$refs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') |
    ForEach-Object { "/r:$framework\WPF\$_" }
$run = Join-Path $PSScriptRoot ("..\..\test-output\tray-" + [guid]::NewGuid().ToString('N'))
$versions = [ordered]@{ baseline = $BaselineExe; candidate = $CandidateExe }
foreach ($item in $versions.GetEnumerator()) {
    $folder = (New-Item -ItemType Directory -Path (Join-Path $run $item.Key) -Force).FullName
    $copy = Copy-Item -LiteralPath $item.Value -Destination $folder -PassThru
    $probe = Join-Path $folder 'FanPilot.UiTests.exe'
    & "$framework\csc.exe" /nologo /optimize+ /platform:x64 /target:winexe "/out:$probe" "/r:$($copy.FullName)" @refs "$PSScriptRoot\MemoryProbe.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
    $process = Start-Process -FilePath $probe -WorkingDirectory $folder -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { throw 'Simulated tray measurement timed out.' }
    $record = Get-Content -LiteralPath (Join-Path $folder 'memory.txt') -Raw
    if ($process.ExitCode -ne 0) { throw $record }
    Write-Output "$($item.Key) $record"
}
