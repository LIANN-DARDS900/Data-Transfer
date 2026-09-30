# Physical qualification matrix

All rows below are **PENDING PHYSICAL QUALIFICATION** until a tester records date, operator, redacted machine pair, application version, evidence path and outcome. Automated CI is evidence only for its separate software rows; it cannot pass physical rows.

## Baseline and data matrix

|Class|Cases|Required evidence|Status|
|---|---|---|---|
|Machine pair|OLD Windows 11 laptop → NEW Windows 11 laptop; 1366×768, 1920×1080, 2560×1440, 3840×2160 at 100/125/150/200%|OS/build, DPI screenshots, keyboard/focus/automation-name and clipping checklist|PENDING PHYSICAL QUALIFICATION|
|Route|NTFS USB HDD; USB SATA SSD; USB NVMe; configured approved UNC|redacted device/share identity, filesystem, plan/report/diagnostics|PENDING PHYSICAL QUALIFICATION|
|Data|1 GB; 10 GB; 100+ GB where available; 10k; 100k+; mixed profile|dataset identity/count/hash-of-inventory, timings|PENDING PHYSICAL QUALIFICATION|
|Known folders|Desktop, Documents, Downloads, Pictures, Videos, Music, Favorites where supported|manifest/report totals and exclusions|PENDING PHYSICAL QUALIFICATION|
|Conflict|none; KeepBoth; repeated collision; racing creation; cancellation|before/after evidence proving every existing file/source intact|PENDING PHYSICAL QUALIFICATION|
|Verify/report|standard; SHA-256; mismatch; failed retry; all final statuses: Success, SuccessWithWarnings, Incomplete, VerificationFailed, Failed, Cancelled|verification record and JSON/HTML report|PENDING PHYSICAL QUALIFICATION|

## External media fault matrix

For every enclosure type test normal transfer, insufficient space, removal before and during execution, same-device reconnect, different-device connect, identity mismatch, read-only destination, filesystem unavailable, and write-probe failure. Confirm safe refusal for non-NTFS according to policy; never format, alter BitLocker, or bypass removable-media policy. **PENDING PHYSICAL QUALIFICATION.**

## Approved UNC fault matrix

Using only the configured approved UNC and current Windows identity test available, unavailable, authorization denied, mid-operation disconnect, reconnect, uncertain capacity, changed approval policy, and a different share than the reviewed plan. Confirm no discovery/probing/SMB change/credential prompt. **PENDING PHYSICAL QUALIFICATION.**

## OneDrive and Outlook matrix

Test OneDrive absent/present with local, pinned, online-only, partially available and unknown/unavailable states. Prove non-hydrated online-only bytes are excluded from migrated bytes and no bulk hydration occurs. Test accessible, locked, multiple and large PSTs plus OST; verify normal PST transfer, readable FileLocked/close-Outlook action, and OST exclusion without profile-migration claims. **PENDING PHYSICAL QUALIFICATION.**

## Interruption/recovery matrix

Terminate during scan/transfer/verification; close app; restart Windows; remove destination; change source/destination; corrupt copies of manifest, journal and verification record. Inspect before Resume and prove session, plan, policy, source/destination identities, manifest, trusted tool/version and free space are revalidated. **PENDING PHYSICAL QUALIFICATION.**
