#requires -Version 5.1
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/network-test-kit'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory to preserve previous evidence.' }
Push-Location $repo
try {
    dotnet test RoboTransfer.sln -c Release
    if ($LASTEXITCODE) { throw 'Tests failed' }
    dotnet publish src/RoboTransfer.App/RoboTransfer.App.csproj -c Release -r win-x64 --self-contained false -o (Join-Path $output 'App')
    if ($LASTEXITCODE) { throw 'Application publish failed' }
    dotnet publish tools/NetworkValidation/RoboTransfer.NetworkValidation.csproj -c Release -r win-x64 --self-contained false -o (Join-Path $output 'NetworkValidation')
    if ($LASTEXITCODE) { throw 'Qualification publish failed' }
    Copy-Item tools/NetworkValidation/Get-NetworkDiagnostics.ps1 $output
    Copy-Item tools/NetworkValidation/Measure-Report.ps1 $output
    Copy-Item tools/NetworkValidation/Invoke-TwoMachineValidation.ps1 (Join-Path $output 'NetworkValidation')
    Copy-Item tools/NetworkValidation/Get-NetworkDiagnostics.ps1 (Join-Path $output 'NetworkValidation')
    Copy-Item docs/validation/Network-Validation-Report.md $output
    Get-ChildItem $output -File -Recurse | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash | Export-Csv (Join-Path $output 'SHA256SUMS.csv') -NoTypeInformation
    Write-Output $output
} finally { Pop-Location }
