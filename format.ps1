param([switch]$Verify, [string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core') { throw 'Formatting requires PowerShell 7 and the .NET 8 SDK.' }
# PowerShell 7 ships Roslyn, so formatting also works without an SDK download.
$roslyn = $PSHOME
if (-not (Test-Path -LiteralPath (Join-Path $roslyn 'Microsoft.CodeAnalysis.CSharp.dll'))) {
    $sdkLine = & $DotnetPath --list-sdks | Where-Object { $_ -match '^8\.' } | Select-Object -Last 1
    if ($sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'Roslyn not found. Use PowerShell 7 or the .NET 8 SDK.' }
    $roslyn = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore'
}
Add-Type -Path (Join-Path $roslyn 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $roslyn 'Microsoft.CodeAnalysis.CSharp.dll')
$method = [Microsoft.CodeAnalysis.SyntaxNodeExtensions].GetMethods() | Where-Object { $_.Name -eq 'NormalizeWhitespace' -and $_.GetParameters().Count -eq 4 } | Select-Object -First 1
$normalizer = $method.MakeGenericMethod([Microsoft.CodeAnalysis.SyntaxNode])
$changes = @()
foreach ($file in Get-ChildItem -LiteralPath "$PSScriptRoot\src", "$PSScriptRoot\tests" -Filter '*.cs' -Recurse) {
    $original = [IO.File]::ReadAllText($file.FullName)
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($original)
    $formatted = $normalizer.Invoke($null, @($tree.GetRoot(), '    ', "`r`n", $false)).ToFullString() + "`r`n"
    if ($formatted -ceq $original) { continue }
    $changes += $file.FullName
    if (-not $Verify) { [IO.File]::WriteAllText($file.FullName, $formatted, [Text.UTF8Encoding]::new($false)) }
}
if ($Verify -and $changes.Count -gt 0) { throw ("Formatting required:`n" + ($changes -join "`n")) }
Write-Output ("Formatting {0}: {1} files changed." -f $(if ($Verify) { 'verified' } else { 'completed' }), $changes.Count)
