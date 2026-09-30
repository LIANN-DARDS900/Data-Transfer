# Release Candidate checklist

RoboTransfer is currently **ALPHA — qualification build**. It may become **RELEASE CANDIDATE — NOT PRODUCTION READY** only when every software-controlled gate below has complete evidence. Production readiness additionally requires controlled physical enterprise pilot evidence and enterprise approval.

## Software-controlled gates

| Gate | Evidence required | Current state |
|---|---|---|
| Strict restore/build/test and NuGet audit | Green Windows CI, zero warnings/errors, automated suites green, no reported vulnerable application dependencies | **PASS IN CI** |
| Deterministic `win-x64` publish | Release-validation artifact and commit-bearing informational version | **PASS IN CI** |
| x64 MSI build and static validation | WiX x64 artifact plus MSI metadata/ICE validation | **PASS IN CI** |
| MSI lifecycle | Fresh install, launch, repair, upgrade, and uninstall evidence on clean Windows VM/endpoints | **PENDING QUALIFICATION** |
| Authenticode readiness | Signing script review and approved secure signing pipeline | **IMPLEMENTED; ENTERPRISE SIGNING APPROVAL PENDING** |
| Robocopy trust | Canonical path/name tests plus real Windows WinVerifyTrust/Microsoft signer evidence | **AUTOMATED PATH COVERAGE PRESENT; REAL-ENDPOINT EVIDENCE PENDING** |
| Durable-state safety | Canonical/reparse/session tests plus corruption/fingerprint suites | **PASS IN AUTOMATED SUITE** |
| KeepBoth/report/diagnostics/recovery | Regression suites green | **PASS IN AUTOMATED SUITE** |
| Qualification tooling/runbooks | Scripts and documents present and reviewed as a qualification package | **IMPLEMENTED; OPERATOR REVIEW PENDING** |

## Physical qualification gates

The following remain **PENDING PHYSICAL QUALIFICATION**:

- Controlled OLD-PC → NEW-PC Windows 11 pair.
- External-media matrix and configured approved-UNC matrix.
- 1 GB, 10 GB, 100+ GB, and high-file-count datasets.
- Known Folders and realistic user data.
- Same-name collisions and `KeepBoth` preservation.
- OneDrive/cloud placeholders.
- PST/OST and large/locked-file cases.
- Interruption, cancellation, restart, resume, and recovery.
- Standard and strong verification, including targeted retry.
- Realistic performance measurement.
- DPI/scaling, accessibility, and technician usability.
- Executable publisher and endpoint allowlisting behavior.
- Evidence for every final report outcome.

## Enterprise/policy gates

The following remain **PENDING ENTERPRISE APPROVAL**:

- Enterprise publisher certificate custody.
- Signing, timestamp, and certificate-chain policy.
- Runtime servicing/update ownership.
- SCCM/MECM packaging and deployment approval.
- Publisher/endpoint allowlisting.
- NTFS, removable-media, and BitLocker controls.
- Approved UNC and current-identity authorization rules.
- Evidence retention and support privacy handling.
- Controlled pilot sign-off.

## Release decision

Do **not** label RoboTransfer Production Ready or deploy it broadly.

Current classification:

**ALPHA — QUALIFICATION BUILD**

Next classification, after the remaining software/VM qualification gates are evidenced:

**RELEASE CANDIDATE — NOT PRODUCTION READY — PENDING PHYSICAL QUALIFICATION**

Next activity:

**CONTROLLED REAL-DEVICE / VM QUALIFICATION**
