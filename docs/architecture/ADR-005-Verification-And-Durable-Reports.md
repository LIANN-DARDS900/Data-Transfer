# ADR-005: Independent verification and durable reports

**Status:** Accepted for Phase 3 BETA-candidate validation

## Decision

Transfer, verification, and reporting are separate operations. A successful transfer enters verification pending; it cannot become `Success` until the policy-required verification mode completes.

Standard verification streams the completed manifest and compares the current source identity, source size/timestamp, destination existence, destination size, and destination timestamp. Strong verification first performs every standard check and then streams both current source and destination through SHA-256. It snapshots size and timestamp before and after hashing to detect concurrent change. Concurrency is intentionally bounded to one file in v1 to minimize endpoint I/O pressure; memory is bounded to two 128 KiB stream buffers. Skipped cloud data is never hashed.

Failed entries are durable and can be retried as a subset identified by Known Folder plus relative path. Unknown, inaccessible, changed, missing, and mismatched items never count as verified.

Verification records use a schema-versioned integrity envelope. JSON and PDF-ready HTML reports derive from the immutable plan, completed manifest, journal, and durable verification result. Reports contain fingerprints and record identities but redact the profile identity and destination path. Final report states are `Success`, `SuccessWithWarnings`, `Incomplete`, `VerificationFailed`, `Failed`, and `Cancelled`.

Structured local diagnostics use rotating JSONL files with timestamp, optional session ID, component, severity, category, and a safe redacted message. Diagnostics export is intentional and local. Credentials, tokens, file contents, and unnecessary full paths are prohibited.

## Consequences

Strong verification rereads every eligible byte twice and can approximately double migration I/O. It provides content equality, not authenticity or malware assessment. Verification and reporting remain local and do not introduce telemetry or a cloud backend. Maturity is **BETA CANDIDATE only after independent Windows CI and real-device qualification evidence**; this is not Production Ready.
