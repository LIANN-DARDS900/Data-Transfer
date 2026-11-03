#requires -Version 5.1
<# Runs on OLD. NEW must already host the approved share. No remote commands,
   share creation, credentials or security configuration changes. Writes synthetic
   files only into a new per-run child of the approved share. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.\-]{0,252}$')][string]$Peer,
    [Parameter(Mandatory)][string]$ApprovedShare,
    [Parameter(Mandatory)][string]$PolicyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,4096)][int]$SizeMiB = 64
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory for each qualification run.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
& (Join-Path $PSScriptRoot 'Get-NetworkDiagnostics.ps1') -Peer $Peer -OutputDirectory (Join-Path $output 'before')
$exe = Join-Path $PSScriptRoot 'RoboTransfer.NetworkValidation.exe'
if (Test-Path -LiteralPath $exe) {
    & $exe --destination $ApprovedShare --policy $PolicyPath --size-mib $SizeMiB --output (Join-Path $output 'evidence')
} else {
    $project = Join-Path $PSScriptRoot 'RoboTransfer.NetworkValidation.csproj'
    if (!(Test-Path -LiteralPath $project)) { throw 'Qualification executable/project is missing.' }
    dotnet run --project $project -c Release -- --destination $ApprovedShare --policy $PolicyPath --size-mib $SizeMiB --output (Join-Path $output 'evidence')
}
$code = $LASTEXITCODE
& (Join-Path $PSScriptRoot 'Get-NetworkDiagnostics.ps1') -Peer $Peer -OutputDirectory (Join-Path $output 'after')
if ($code) { throw "Qualification failed or was blocked (exit $code). Review the saved evidence." }
Write-Output (Join-Path $output 'evidence/qualification.html')
