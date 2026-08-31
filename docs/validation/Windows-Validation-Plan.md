# Windows 11 real-device validation plan

## Objective and evidence

Reproduce Phase 3 detection, transfer, verification, reporting, and policy decisions on managed Windows 11 x64 endpoints. Record OS build, RoboTransfer commit, device model, user privilege, policy file hash, screenshots, structured logs, expected result, actual result, and pass/fail for every case. Never use production employee data; use a synthetic test profile and approved test destinations.

Exercise standard and strong verification on zero-byte, large, locked, changed, missing, mismatched, cloud-skipped, long-path, and access-denied files. Confirm cancellation leaves a retryable record and retry processes only failed identities. Validate JSON/HTML schema, redaction, final-state mapping, rotating diagnostics, corrupted manifest/verification rejection, 1366×768 through 4K scaling, and strong-verification I/O impact on representative external and approved network storage.

## Baseline procedure

1. Publish a Release `win-x64` build from a clean checkout and verify its hash.
2. Place a version-1 policy in `%LOCALAPPDATA%\RoboTransfer\policy.json`; record its SHA-256.
3. Start as a standard user, select **Refresh analysis**, and capture environment, source, and recommendation states.
4. Compare logical volumes with Disk Management, `Get-Disk`, `Get-PhysicalDisk`, and `Get-Volume` evidence. Do not change device configuration.
5. Compare selectable profiles with `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList`; verify Public, Default, service, temporary, and stale profiles are absent.
6. Compare known-folder resolution with the selected profile's registered User Shell Folders and OneDrive Known Folder Move state.
7. Repeat the defined case, restart the application, and confirm results are deterministic.
8. Save redacted evidence under the test-run identifier. Restore the endpoint to its initial state.

## Required matrix

| ID | Endpoint / condition | Expected behavior |
|---|---|---|
| WIN-01 | Corporate Windows 11 laptop, standard user, internal NVMe only | Runs without elevation; internal NVMe is not external media |
| WIN-02 | Same laptop, administrator execution | Elevation changes to administrator; route results do not become permissive |
| STO-01 | USB flash drive | External candidate with USB/removable evidence and accurate free capacity |
| STO-02 | External USB SSD reported by Windows as fixed | External candidate because associated disk reports USB |
| STO-03 | USB NVMe enclosure | External USB attachment; model/bus evidence retained without claiming internal NVMe |
| STO-04 | Fixed disk with missing WMI association | Attachment `Unknown`; never selected as external media |
| STO-05 | Destination below estimated capacity | Planner rejects route with capacity explanation |
| CLD-01 | OneDrive enabled with hydrated and pinned files | Locally available/pinned classifications agree with Windows state |
| CLD-02 | OneDrive online-only and partially hydrated files | Never described as migrated; unknown/online state blocks preparation pending review |
| USM-01 | Supported ADK with ScanState and LoadState | USMT available, install path reported, nothing executed |
| USM-02 | USMT absent or only one executable present | USMT not available; application remains usable |
| NET-01 | Policy-approved UNC accessible with current identity | Only exact configured path checked and reported accessible |
| NET-02 | Approved UNC unavailable/access denied | Not available with actionable explanation; no other host scanned |
| POL-01 | Restrictive policy while USB/share/tool technically available | Every forbidden mechanism remains ineligible |
| POL-02 | Malformed and unsupported policy files | Conservative mode with visible validation error; never permissive fallback |
| PRO-01 | Current, redirected, stale, Public, Default, service, temporary profiles | Only authoritative interactive registered profiles selectable; resolution confidence shown |
| REC-01 | Valid incomplete and corrupt journals | Valid incomplete session discoverable; corrupt journal rejected, never treated as resumable |

## Scale and display checks

Run at 1366×768, 1920×1080, and one 1440p/4K display at Windows scaling 100%, 125%, 150%, and 200%. Verify no clipped workflow, status, source selector, warning, or recommendation; tab order and visible focus must remain usable with keyboard only. Use Narrator to verify labels and control purposes.

## Exit criteria

All required cases pass on at least two corporate hardware models; no high-severity security finding remains; Release build/test artifacts are clean; UI accessibility review passes; WMI/profile/Cloud Files discrepancies are documented and represented as Unknown rather than guessed.
