# ADR-004: Operational migration engine v1

**Status:** Accepted for ALPHA controlled technician testing

## Decision

RoboTransfer owns selection, streaming scan, an immutable execution snapshot, destination revalidation, conflict preparation, lifecycle, and recovery. Robocopy remains a replaceable transfer adapter and is launched directly with `ProcessStartInfo.ArgumentList`; no shell accepts user input.

The scanner accepts only selected, authoritatively resolved Desktop, Documents, Downloads, Pictures, Videos, Music, and Favorites roots. It enumerates incrementally, canonicalizes every path, never follows reparse points, and writes one JSON object per line. File contents and hashes are never persisted. `.ost` caches are excluded; accessible `.pst` files are ordinary data.

Cloud states are truthful: locally available and pinned entries are eligible. Online-only, partially available, unavailable, and unknown entries are recorded but skipped with a warning. Version 1 never hydrates cloud data.

## Transfer policy

Baseline Robocopy arguments are `/E /COPY:DAT /DCOPY:T /R:2 /W:2 /XJ /Z /NP /BYTES`. `/MIR`, move switches, source deletion, ACL/owner copying, arbitrary arguments, and shell execution are prohibited. Skip adds `/XC /XN /XO`; ReplaceIfSourceNewer adds `/XO`. KeepBoth and ManualDecision require the controlled preparation layer. Replace requires policy permission plus explicit technician confirmation.

Exit codes 0–7 are nonfatal bitmask outcomes; 8 and above are failures. English output is never authoritative. Output capture is asynchronous and bounded to 64 recent lines per stream. Cancellation terminates the process tree and produces an interrupted result.

## Destination and recovery

Validation rejects overlapping source/destination trees, protected OS/application locations, unapproved shares, changed or non-external media, unknown filesystems, insufficient space, changed policy, and unwritable targets. A temporary write probe is deleted immediately. Validation must run again before start or resume.

Prepared, Transferring, Interrupted, Completed, Failed, and Abandoned journal states are operational. Resume verifies session/manifest identity, manifest availability, policy fingerprint, Robocopy path, device identity, free capacity, and destination safety. A mismatch fails closed.

## Consequences and limitations

Phase 2 transfers selected known-folder data only. It excludes AppData/profile cloning, OST caches, credentials, browser secrets, USMT execution, automatic hydration, strong content hashing, final verification, and report generation.

JSONL uses a versioned metadata record, streaming entry records, and a final authoritative completion footer. A missing footer is incomplete and malformed data is corrupt; neither can be resumed. KeepBoth uses per-item `CreateNew` reservations beneath the canonical destination root, so races cannot overwrite existing files. Reconciliation compares eligible manifest counts and bytes to transfer results, then enters `TransferCompletedVerificationPending`; it never claims strong verification.

The Avalonia composition root discovers incomplete sessions at startup. The technician workflow provides Source selection, cancellable Scan, immutable Plan Review, destination revalidation, Transfer monitoring, and Inspect/Resume/Abandon recovery actions. Verification and Report remain visible but inactive next-phase stages.

Memory remains bounded by directory depth, one serialized manifest record, and 64 recent lines per process stream. This release is **ALPHA**, not production ready.
