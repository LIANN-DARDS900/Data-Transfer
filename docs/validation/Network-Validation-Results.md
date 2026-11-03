# Executed results — 2026-11-03

## Scope and provenance

- [Windows run 41628398716](https://github.com/LIANN-DARDS900/Data-Transfer/actions/runs/41628398716), job 125891467100, completed successfully.
- Application/test source: branch commit `8b56892728cd562df4987f78f8f5e8da365d47f2`, based on PR #7 commit `5481bb1e81a78c074bd34ebb7e59a86d909ce762`. GitHub ran the PR merge revision; no PR was merged into the repository's main branch.
- One disposable Windows Server 2025 hosted runner. **No second Windows machine, physical Dell, RJ45 cable, OCP firewall or endpoint was tested.**
- Tests: **122 core + 42 Windows = 164 passed, 0 failed**. PowerShell scripts parsed using Windows PowerShell 5.1; the read-only collector ran on that runner. Application and qualification executable published successfully for Windows x64.
- Local supporting validation: .NET 10.0.403, 122 core tests passed; harness compiled with zero warnings/errors; application and harness cross-published for win-x64. Initial parallel MSBuild was blocked by local IPC permissions; serial compilation succeeded. No permission escalation or host security change was used.

## Measured copy performance

| Environment | Eligible files | Logical bytes | First-copy elapsed | Effective copy MiB/s | SHA-256 completion |
|---|---:|---:|---:|---:|---|
| Runner local filesystem, 64 MiB random large fixture + small/inert files | 115 | 67,211,902 | 0.6288561 s | 101.93 | 115/115 PASS |
| Same runner, encrypted SMB loopback, 16 MiB random large fixture + small/inert files | 115 | 16,880,254 | 2.2968017 s | 7.01 | 115/115 PASS |
| Two physical Dell laptops over the cable | — | — | — | **NOT MEASURED** | NOT RUN |

These are short synthetic smoke runs, one sample per environment, including many small files. They are not sustained throughput benchmarks and are not comparable as an SMB performance penalty measurement (dataset sizes differ). Copy rate uses completed logical bytes divided by engine elapsed, excludes generation/scan/hashing, and is not a wire-speed measurement. Both ends of the loopback test were the SAME Windows VM.

The loopback share required encryption and granted access only to the existing runner identity. `Get-SmbConnection` reported **dialect 3.1.1, Encrypted=True, Signed=False**. This records the observed flags; it is not a request to change signing policy. No signing, firewall, service or endpoint policy was modified. The temporary lab share was removed.

## PASS/FAIL matrix

| Expectation | Local | Loopback SMB | Evidence |
|---|---|---|---|
| Default engine copies all eligible files and reconciles counts | PASS | PASS | 115 files each; TransferCompletedVerificationPending |
| Strong verification confirms the initial copy | PASS | PASS | 115 SHA-256 matches each |
| OST cache fixture is excluded | PASS | PASS | One scan exclusion; OST absent |
| Equal-size deliberate corruption fails strong verification | PASS | PASS | ContentMismatch and mismatch count=1 |
| Injected cancellation reports cancellation and removes owned partial file | PASS | PASS | No partial file remained in the controlled fixture |
| Full-file retry after injected cancellation completes | PASS | PASS | Copy and independent verification succeeded |
| Resume avoids duplicating completed files | **FAIL** | **FAIL** | Repeating the manifest created **115 additional copies** each |
| Verification checks the exact most recently copied collision candidate | **FAIL** | **FAIL** | Corrupting the renamed copy while retaining the correct original went undetected: verifier chose the older metadata match |
| Exclusive locked PST is reported as FileLocked | PASS | PASS | Real OS sharing lock; inert fixture, no Outlook |
| Actual system Robocopy adapter copies fixture bytes | PASS | PASS | Separate adapter execution and byte comparison |
| TCP 445 deny rules, bad credentials, unplug/reconnect | NOT RUN | NOT RUN | Requires two independent authorized endpoints |

Each environment exercised 8 passing expectations, 2 known requirement failures and 1 unexecuted scenario group. CI's success means the characterization harness completed and its prerequisite checks passed; it does **not** mean those two failing requirements passed.

The five error-classification regression cases distinguish injected Windows errors 32/33 from network errors 53/64. They do not reproduce a real network interruption or prove the source of a corporate block.

## How the transfer finished

Both first copies reached **TransferCompletedVerificationPending**, then independently verified **115/115 eligible files** and all expected bytes. The scanner deliberately excluded the OST fixture. The harness saved per-file verification locally on the runner, and the summary matrices were retained in the CI log/job summary. No endpoint diagnostic report or user data was uploaded.

This engine-level harness does not drive the GUI, merge files into a live employee profile, or exercise the full receiver UI across hosts. Existing receiver-coordinator tests remain part of the 164 passing tests. The qualification kit includes the actual app so those physical UI steps can be performed under the runbook.

## Blockages and remaining gates

1. **Application correctness:** fix durable per-session source/destination receipts before qualifying collision handling or resume. Byte-offset resume is not implemented in KeepBoth.
2. **Infrastructure:** no connected two-machine Windows lab or approved cloud account was available. Standard public CI provided one VM plus loopback SMB; no private two-host environment was provisioned.
3. **Physical environment:** actual link speed, cable condition, routing through Ethernet, managed SMB permissions, OCP restrictions and real Dell throughput are unknown until the supplied collectors and test kit run on the pair.
4. **File-policy evidence:** inert extension fixtures copied; `.ost` was excluded by the app. This does not certify real Office/PST content, EFS/ADS, OneDrive or corporate DLP. No claim is made that OCP blocks a particular extension.
5. **Deployment:** unsigned framework-dependent qualification payload; approved .NET 10 runtime is required. GUI launch, enterprise allowlisting/signing and installer lifecycle were not validated by this new workflow.
6. **Cloud/CI costs:** no billable cloud deployment or artifact/cache upload was introduced. Standard public-repository Windows compute only.

## Delivered tools

- `Get-NetworkDiagnostics.ps1`: read-only local collection, optional one-host TCP 445 probe.
- `Invoke-TwoMachineValidation.ps1`: automated source-side synthetic test against an existing policy-approved receiver share; requires no remote execution service.
- `RoboTransfer.NetworkValidation.exe`: real scanner/copy/verification/Robocopy harness; no protection changes.
- `Measure-Report.ps1`: reads the app's final JSON and computes copy-stage throughput.
- `Build-TestKit.ps1`: reproduces the qualification payload.
- `Network-Validation-Report.md`: architecture, file handling, diagnostic decision table, protocol evaluation and physical procedure.

The delivered build is **ready for controlled testing, not production migration approval**. The exact final documentation/diagnostic revision and any follow-up validation are listed in the pull request. Main and PR #7 remain unmerged.
