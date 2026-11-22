param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Test }
$executable = Join-Path $PSScriptRoot 'bin\app\ASUS FanPilot Dial.exe'
$version = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
$folder = Join-Path $PSScriptRoot "dist\ASUS-FanPilot-Dial-v$version"
New-Item -ItemType Directory -Path $folder -Force | Out-Null
# Explicit allowlist: no test executables, personal logs or vendor binaries.
Copy-Item -LiteralPath $executable -Destination $folder -Force
foreach ($name in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'setup-dependencies.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $folder -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\QUICKSTART.md') -Destination (Join-Path $folder 'README.md') -Force
$assembly = if ($PSVersionTable.PSEdition -eq 'Core') { [Reflection.Assembly]::LoadFile($executable) } else { [Reflection.Assembly]::ReflectionOnlyLoadFrom($executable) }
if ($assembly.GetManifestResourceNames() | Where-Object { $_ -match 'PsExec|AsusWinIO' }) { throw 'Release contains embedded vendor binaries.' }
$hash = (Get-FileHash -LiteralPath (Join-Path $folder 'ASUS FanPilot Dial.exe') -Algorithm SHA256).Hash
"$hash  ASUS FanPilot Dial.exe" | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Encoding ASCII
$allowed = @('ASUS FanPilot Dial.exe', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'setup-dependencies.ps1', 'README.md', 'SHA256SUMS.txt')
if (Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.DirectoryName -ne $folder -or $_.Name -notin $allowed }) { throw 'Unexpected files in release folder.' }
$archive = "$folder.zip"
Compress-Archive -LiteralPath $folder -DestinationPath $archive -Force
Write-Output "Release package: $archive"
