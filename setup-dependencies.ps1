param(
    [Parameter(Mandatory = $true)][string]$AsusLibrary,
    [Parameter(Mandatory = $true)][string]$PsExec,
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
# Supply unmodified copies obtained under their respective vendor terms.
# This script does not download software, accept licenses, or launch executables.
$target = if ($Destination) { [IO.Path]::GetFullPath($Destination) } else { Join-Path $env:LOCALAPPDATA 'FanPilotDial\runtime-v2' }
$files = @(
    @{ Source = $AsusLibrary; Name = 'AsusWinIO64.dll'; Hash = 'DAC0A7765BBDD3B66ED9B1222BB7FA630B5ABC195E5BCE4758AA05AE880B2166' },
    @{ Source = $PsExec; Name = 'PsExec.exe'; Hash = '078163D5C16F64CAA5A14784323FD51451B8C831C73396B967B4E35E6879937B' }
)
foreach ($file in $files) {
    $actual = (Get-FileHash -LiteralPath $file.Source -Algorithm SHA256).Hash
    if ($actual -ne $file.Hash) { throw "Unsupported or modified dependency: $($file.Name). See THIRD-PARTY-NOTICES.md." }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($file in $files) {
    $source = (Resolve-Path -LiteralPath $file.Source).Path
    $destinationPath = Join-Path $target $file.Name
    if ($source -ne $destinationPath) { Copy-Item -LiteralPath $source -Destination $destinationPath -Force }
}
Write-Output "Verified dependencies installed in $target"
