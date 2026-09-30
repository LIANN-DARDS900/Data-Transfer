# Engineering roadmap

Current status: **ALPHA — qualification build.** The core migration workflow is implemented and software CI is green, but RoboTransfer is **not production ready** until physical qualification, installer lifecycle testing, enterprise signing/deployment approval, and a controlled pilot are complete.

## Phase 1 — Foundation ✅

Implemented and validated in Windows CI:

- Policy-first capability detection and deterministic planning.
- Windows storage, profile, known-folder, cloud-placeholder, elevation, Robocopy, USMT, and approved-share detection.
- Conservative fail-closed policy loading and session journaling.
- Avalonia technician shell and guided environment/source planning flow.

## Phase 2 — Migration Engine ✅

Implemented and covered by automated tests:

- Streaming reparse-safe manifest scanning.
- Immutable execution plans.
- Constrained Robocopy execution using safe argument construction.
- Finite retry behavior, cancellation, and preservation-first `KeepBoth` conflict handling.
- Destination validation, capacity/safety gates, transfer reconciliation, and local checkpoint state.
- Incomplete-session Inspect, validated Resume, and Abandon paths.

## Phase 3 — Verification & Reports ✅

Implemented and covered by automated tests:

- Independent standard verification.
- SHA-256 strong verification of current source and destination content.
- Targeted verification retry.
- Durable redacted JSON and PDF-ready HTML technician reports.
- Structured diagnostics, explicit final outcomes, and recovery/reporting separation from transfer.

## Qualification & Packaging — CURRENT 🚧

Software-controlled work implemented in the production-readiness branch:

- Explicit application/version identity.
- Hardened local application-data layout.
- Canonical Windows Robocopy Authenticode trust validation.
- Framework-dependent and self-contained `win-x64` release profiles.
- Unsigned WiX x64 qualification MSI.
- Release-validation CI for restore, build, tests, dependency audit, application publish, MSI build, and MSI validation.
- Technician, deployment, troubleshooting, performance, real-device, and release-candidate documentation.

Still required before Release Candidate classification:

- Fresh install, repair, upgrade, and uninstall evidence on clean Windows VM/endpoints.
- Real Windows validation of executable publisher/trust behavior under enterprise policy.
- Review of release scripts and qualification artifacts as a complete package.

## Physical Qualification — NEXT ⏳

Run the controlled OLD-PC → NEW-PC qualification matrix on managed Windows 11 x64 devices:

- Known Folders and realistic user datasets.
- External media and approved UNC routes.
- 1 GB, 10 GB, and 100+ GB datasets plus high file counts.
- Same-name collisions and `KeepBoth` preservation.
- Cancellation, interruption, restart, resume, and incomplete-session recovery.
- Standard and strong verification plus failed-entry retry.
- OneDrive/cloud placeholder behavior.
- PST/OST and other large/locked-file cases.
- Performance, DPI/scaling, accessibility, and technician usability.
- Every final report outcome.

Successful physical qualification may advance the project to **RELEASE CANDIDATE — NOT PRODUCTION READY**.

## USMT Operational Integration — FUTURE ⏳

USMT is currently detection-only. Future work must:

- Validate supported installed ADK versions and executable signatures.
- Implement enterprise-approved ScanState/LoadState configuration.
- Orchestrate store creation/restoration only over an already approved route.
- Parse outcomes and journal profile/settings migration separately from known-folder files.
- Preserve all current policy, reporting, and non-bypass guarantees.

USMT execution is not required to prove the current Robocopy Known Folders migration path.

## Production Pilot — FINAL GATE ⏳

Before broad enterprise deployment:

- Enterprise publisher certificate custody and signing/timestamp/chain policy.
- Executable publisher and endpoint allowlisting approval.
- SCCM/MECM package and deployment validation.
- Runtime servicing/update ownership.
- NTFS, removable-media, BitLocker, approved-UNC, and current-identity policy validation.
- Evidence retention, diagnostics privacy, and support ownership.
- Threat-model and accessibility sign-off.
- Controlled enterprise pilot with documented acceptance criteria.

Only after these gates should RoboTransfer be considered for **Production Ready** status.
