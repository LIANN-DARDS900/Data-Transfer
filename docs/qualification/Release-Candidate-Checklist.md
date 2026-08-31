# Release Candidate checklist

Maturity can be **RELEASE CANDIDATE** only after every software-controlled row is evidenced by green Windows runs. It is **NOT PRODUCTION READY**. Production readiness requires controlled physical enterprise pilot evidence.

## Software-controlled gates

|Gate|Evidence required|State|
|---|---|---|
|Strict restore/build/test and NuGet audit|green `windows-ci` run, logs, zero warnings/errors/vulnerabilities|PENDING CI EVIDENCE|
|Deterministic win-x64 publish|release-validation artifact and commit-bearing informational version|PENDING CI EVIDENCE|
|x64 MSI|WiX build artifact; metadata/ICE validation; fresh/upgrade/uninstall VM evidence|PENDING CI EVIDENCE|
|Authenticode readiness|script dry-run review; secure signing pipeline approval is separate|IMPLEMENTED, VALIDATION PENDING|
|Robocopy trust|canonical-name/location tests and real Windows WinVerifyTrust/Microsoft signer test|PENDING CI EVIDENCE|
|Durable-state safety|canonical/reparse/session tests plus corruption/fingerprint suites|PENDING CI EVIDENCE|
|KeepBoth/report/diagnostics/recovery|existing and Phase 4 regression suites green|PENDING CI EVIDENCE|
|Qualification tooling/runbooks|reviewed scripts and documents|IMPLEMENTED, REVIEW PENDING|

## Physical qualification gates

Old/new Windows 11 pair, external-media matrix, configured UNC matrix, 1/10/100+ GB and 10k/100k+, Known Folders, OneDrive, PST/OST, interruption/recovery, realistic performance, DPI/scaling, accessibility, executable publisher/endpoint allowlisting, and every final report outcome are **PENDING PHYSICAL QUALIFICATION**.

## Enterprise/policy gates

Enterprise publisher certificate custody, signing/timestamp/chain policy, runtime servicing approval, SCCM/MECM packaging, allowlisting, NTFS/removable-media/BitLocker controls, approved UNC/current-identity authorization, evidence retention, support privacy handling, and controlled pilot sign-off are **PENDING ENTERPRISE APPROVAL**.

## Release decision

Do not label Production Ready or deploy broadly. After green software gates the exact classification is **RELEASE CANDIDATE — NOT PRODUCTION READY — PENDING PHYSICAL QUALIFICATION**. Next activity: **CONTROLLED REAL-DEVICE PILOT / QUALIFICATION**.
