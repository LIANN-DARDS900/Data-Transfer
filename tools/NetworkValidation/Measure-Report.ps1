#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Report)
$r = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
$elapsed = [TimeSpan]::Parse($r.TransferElapsed, [Globalization.CultureInfo]::InvariantCulture)
$seconds = $elapsed.TotalSeconds
[pscustomobject]@{
    Session = $r.SessionId
    FinalStatus = $r.FinalStatus
    ExpectedFiles = $r.ExpectedFiles
    TransferredFiles = $r.TransferredFiles
    TransferredBytes = $r.TransferredBytes
    CopySeconds = $seconds
    EffectiveMiBPerSecond = $(if ($seconds -gt 0) { [math]::Round($r.TransferredBytes / $seconds / 1MB, 2) } else { $null })
    EffectiveMegabitsPerSecond = $(if ($seconds -gt 0) { [math]::Round(8 * $r.TransferredBytes / $seconds / 1000000, 2) } else { $null })
    Verification = $r.StrongVerification
    Failures = $r.Failures
    ExcludedItemsAllScanReasons = $r.SkippedCloudContent
    Interpretation = 'Logical completed bytes per copy-stage time. Includes copy-stage overhead; excludes scan and hashing. Not Ethernet wire rate. Does not prove that the route used RJ45.'
}
