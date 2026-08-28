# Engineering roadmap

Current status: **Development**. Phase 1 establishes safe analysis and planning; it is not an operational migration release.

## Phase 2 — Migration Engine

Implement a streaming, reparse-safe manifest scanner; immutable reviewed plan; Robocopy `ITransferEngine` using `ProcessStartInfo.ArgumentList`; finite retries; restartable non-destructive switches; bounded progress parsing; cancellation; preservation-first conflict policies; atomic checkpoint journaling; destination identity revalidation; resume/inspect/abandon. Gate execution on validated policy, sufficient reserved capacity, cloud-content disposition, and technician confirmation.

## Phase 3 — Verification & Reports

Implement independent standard verification and bounded-concurrency SHA-256 verification of both sides; persist per-entry outcomes; targeted retry of failures; signed/portable technician report; redacted diagnostics export; failure summaries that never conflate transfer with verification.

## Phase 4 — USMT

Validate supported installed ADK versions and signatures; implement approved ScanState/LoadState configuration; orchestrate store creation/restoration over an already approved route; parse outcomes; journal and report profile/settings migration separately from known-folder files.

## Phase 5 — Production Packaging

Add semantic build versioning, reproducible self-contained `win-x64` publishing, code signing, MSI install/uninstall, SCCM/MECM deployment guidance, update ownership, accessibility sign-off, threat-model review, performance qualification, and the complete managed-endpoint validation matrix before a production pilot.
