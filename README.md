# RoboTransfer

RoboTransfer is a policy-aware Windows laptop migration orchestrator for technicians moving an employee from an **OLD corporate PC** to a **NEW corporate PC**. It analyzes the endpoint, separates technical capability from enterprise authorization, creates a deterministic migration plan, performs preservation-first transfers, verifies the result, and produces a durable technician report. It is not a generic Robocopy GUI.

> **RoboTransfer does not bypass enterprise network or endpoint security controls. It operates only through migration mechanisms explicitly allowed by policy/configuration.**

## Current status

**ALPHA — qualification build. Not production ready.**

The core OLD-PC → NEW-PC workflow is implemented through:

**Environment → Source → Scan → Migration Plan → Transfer → Verification → Report**

Windows CI validates restore, Release build, automated tests, and dependency vulnerability checks. The release-validation workflow also validates a framework-dependent `win-x64` publish and an unsigned x64 qualification MSI. Real-device qualification, enterprise signing, deployment approval, and a controlled pilot are still required before production use.

See [Release Candidate checklist](docs/qualification/Release-Candidate-Checklist.md) for the remaining gates.

## Implemented capabilities

### Environment and planning

- Read-only OS, architecture, machine, user-context, elevation, registered-profile, known-folder, logical-volume, and physical-disk analysis.
- USB/fixed/removable attachment classification with explicit uncertainty instead of unsafe assumptions.
- System Robocopy detection and Windows ADK USMT detection.
- Cloud-placeholder classification for local, pinned, online-only, partial, unavailable, and unknown states.
- Versioned local JSON policy with strict parsing, safe UNC allow-listing, route/tool permissions, verification level, and preservation-first conflict policy.
- Deterministic planning ordered by policy → capability → capacity → strategy.

### Migration engine

- Streaming, schema-versioned JSONL manifests with completion evidence.
- Immutable execution plans and local session state.
- Constrained Robocopy execution with safe argument policy.
- `KeepBoth` collision handling that preserves existing destination files and never modifies the source.
- Transfer reconciliation against eligible manifest file counts and bytes.
- Cancellation plus incomplete-session discovery for Inspect, validated Resume, or Abandon.
- Destination validation and write probing before execution.

### Verification and reporting

- Standard verification of source metadata and destination existence, size, and timestamp.
- Strong verification using SHA-256 on current source and destination content.
- Detection of source changes during hashing.
- Targeted retry of failed verification entries without rerunning the whole migration.
- Durable redacted JSON and PDF-ready HTML reports.
- Explicit final states: `Success`, `SuccessWithWarnings`, `Incomplete`, `VerificationFailed`, `Failed`, and `Cancelled`.
- Structured local diagnostics and rotating logs.

### Qualification and packaging

- Explicit application/version identity.
- Controlled `%LOCALAPPDATA%\RoboTransfer` state layout with canonical-path and reparse-point checks.
- Windows Authenticode validation for the canonical `%SystemRoot%\System32\robocopy.exe`, with optional Microsoft-publisher enforcement.
- Reproducible framework-dependent and self-contained `win-x64` release profiles.
- Unsigned x64 qualification MSI using WiX.
- Release-validation CI for build, tests, dependency audit, application publish, MSI build, and MSI validation.
- Technician, deployment, troubleshooting, qualification, and release-candidate runbooks under [`docs`](docs).

## Security and privacy posture

- Missing, malformed, or unsupported policy fails closed to a conservative profile.
- Network checks are restricted to explicit validated UNC paths; there is no LAN discovery, subnet scan, port scan, or listener.
- The application does not change firewall, Defender, Group Policy, PowerShell, SMB, services, drivers, or endpoint-protection settings.
- Robocopy runs only through the constrained adapter and only when policy allows it.
- USMT remains detection-only; operational ScanState/LoadState orchestration is not yet implemented.
- Fixed disks are not assumed to be internal or external; uncertain evidence remains `Unknown`.
- Registered Windows profiles and shell-folder mappings replace blind `C:\Users` enumeration and path guessing.
- Transfer and verification are separate states. Transfer success never implies verification success.
- There is **zero network telemetry**, no analytics, no crash upload, and no cloud-service dependency.

See the [threat model](docs/security/Threat-Model.md) and [Windows validation plan](docs/validation/Windows-Validation-Plan.md).

## Planner rules

1. Policy authorization is evaluated first and cannot be overridden by detected hardware or tools.
2. A network route is eligible only when enabled, explicitly allow-listed as an absolute UNC path, and accessible using the current identity.
3. External media is eligible only when policy permits it, Windows evidence classifies it as external, the volume is ready, and confirmed capacity is sufficient.
4. An approved reachable share is preferred over eligible external media; this is deterministic, not a performance guess.
5. USMT is preferred only when installed and operational use is allowed; otherwise Robocopy Known Folders is used when allowed.
6. Unknown or online-only cloud state can block migration preparation for technician review.
7. No eligible route or strategy produces a blocked plan requiring manual action.

## Solution structure

| Project | Responsibility |
|---|---|
| `RoboTransfer.Core` | Domain, policy, planning, execution, recovery, state, and shared contracts |
| `RoboTransfer.Windows` | Windows storage, profile, known-folder, cloud, elevation, and approved-share detection |
| `RoboTransfer.Robocopy` | Constrained Robocopy detection, execution, argument policy, and executable trust validation |
| `RoboTransfer.Usmt` | Existing Windows ADK USMT capability detection; execution remains future work |
| `RoboTransfer.Persistence` | Policy, manifests, execution plans, verification/operation state, and journals |
| `RoboTransfer.Verification` | Independent standard and SHA-256 verification |
| `RoboTransfer.App` | Avalonia technician UI and workflow coordination |
| `RoboTransfer.Core.Tests` | Core planning, migration, recovery, verification/reporting, and production-readiness tests |
| `RoboTransfer.Windows.Tests` | Windows storage/profile/share/tool boundaries and cancellation tests |
| `RoboTransfer.Installer` | Unsigned x64 qualification MSI |

Important decisions are recorded in [`docs/architecture`](docs/architecture). Current work and remaining gates are tracked in the [engineering roadmap](docs/architecture/Roadmap.md).

## Build and run

Requirements: .NET 10 SDK. Runtime qualification requires managed Windows 11 x64 hardware.

```powershell
dotnet restore RoboTransfer.sln
dotnet build RoboTransfer.sln -c Release --no-restore
dotnet test RoboTransfer.sln -c Release --no-build
dotnet run --project src/RoboTransfer.App/RoboTransfer.App.csproj
```

## Qualification release build

The release script builds, tests, and publishes the application. The framework-dependent profile also builds the unsigned qualification MSI.

```powershell
.\build\Release.ps1 -Profile FrameworkDependent -VersionSuffix rc.1
```

A self-contained `win-x64` publish can be produced with:

```powershell
.\build\Release.ps1 -Profile SelfContained -VersionSuffix rc.1
```

These are **qualification artifacts**, not approved enterprise production packages. Code signing, certificate custody, deployment approval, allowlisting, real-device evidence, and pilot sign-off remain separate gates.

## Local policy

Copy [`config/policy.example.json`](config/policy.example.json) to `%LOCALAPPDATA%\RoboTransfer\policy.json`, then have the enterprise policy owner explicitly enable only approved mechanisms. The shipped example and application default deny all routes and tools. Unsupported versions, invalid JSON, unsafe conflict defaults, and invalid UNC entries remain conservative and are surfaced in the UI; they never fall back permissively.

## Still pending

- Controlled OLD-PC → NEW-PC physical qualification on managed Windows 11 hardware.
- External-media and approved-UNC real-device matrices.
- Large-file, high-file-count, interruption/resume, OneDrive, PST/OST, DPI/accessibility, and performance qualification.
- Fresh install, upgrade, uninstall, and repair evidence for the MSI on clean Windows VMs/endpoints.
- Enterprise Authenticode certificate/signing/timestamp pipeline and publisher allowlisting.
- SCCM/MECM packaging and controlled deployment approval.
- Operational USMT ScanState/LoadState integration.
- Controlled enterprise pilot and final production sign-off.
