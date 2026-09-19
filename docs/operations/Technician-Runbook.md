# Technician runbook

## Evidence and paths

RoboTransfer binaries are read-only under `%ProgramFiles%\RoboTransfer`. Durable per-user state is separated under `%LOCALAPPDATA%\RoboTransfer`: `Sessions`, `Manifests`, `Plans`, `Verification`, `Reports`, `Diagnostics`, and `Policies`. Windows profile ACLs and enterprise endpoint controls remain part of the security boundary. RoboTransfer rejects reparse-point state roots, uses canonical child paths and atomic durable record writes; it does not claim protection against an administrator or a process already acting as that user.

Record the displayed application version and session ID. Review source profile, known folders, route/destination identity, plan/policy fingerprints, expected counts, cloud exclusions and verification mode before locking the plan. Standard verification checks size/metadata; strong verification reads both sides and compares SHA-256. Reports identify transferred, skipped, failed, verified, warnings, resumability, and durable evidence references without exposing full profile/path details.

## Routes and special content

External storage qualification requires NTFS and an approved USB HDD, SATA SSD, or NVMe enclosure. Never format media or alter BitLocker. A different/reconnected device must pass identity, capacity, filesystem and write-probe checks. Approved UNC use is limited to a configured path with the current Windows identity: do not discover shares, collect credentials, or change SMB. Capacity may remain uncertain and must be reviewed.

OneDrive online-only/unknown content is recorded as skipped and is not counted as migrated bytes; RoboTransfer performs no hydration and accepts no credentials. Accessible PST files transfer normally. For `FileLocked`, close Outlook and retry; never kill or unlock Outlook. OST files remain excluded and Outlook profiles/configuration are not migrated.

## Recovery and common actions

After app/process termination, restart, disconnect, or corruption, use **Inspect** first. Resume revalidates session, immutable plan, policy fingerprint, source and destination identities, completed manifest, Robocopy identity/version, and free space; it never blindly continues. Corrupt journal/manifest/verification evidence blocks resume. Preserve records and export redacted diagnostics.

* Destination disconnected: reconnect the reviewed destination, Inspect, then Resume.
* Insufficient space: free capacity on the reviewed destination or make a new reviewed plan for another approved destination.
* PST locked: close Outlook and retry the item.
* Verification mismatch: inspect the failed subset; retransfer safely and retry failed verification.
* Access denied/read-only: restore authorized access through normal endpoint policy; do not weaken security.

Reports are in `Reports`; diagnostics in `Diagnostics`; local policy is `Policies\policy.json`. HTML is PDF-ready and JSON enums are stable strings. Do not send raw state externally without approved support handling.
