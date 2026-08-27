# RoboTransfer

RoboTransfer is a policy-aware Windows laptop migration orchestrator for technicians moving an employee from an **OLD corporate PC** to a **NEW corporate PC**. It analyzes the endpoint, distinguishes technical capability from enterprise authorization, and produces a deterministic, explainable migration plan. It is not a generic Robocopy GUI.

> **RoboTransfer does not bypass enterprise network or endpoint security controls. It operates only through migration mechanisms explicitly allowed by policy/configuration.**

**Lifecycle status: Development.** Phase 1 is a non-destructive analysis and architecture foundation. It is not approved for production migrations and contains no active transfer implementation.

## Security and privacy posture

- Missing, malformed, or unsupported policy fails closed to a conservative profile.
- Network checks are restricted to explicit, validated UNC paths; there is no LAN discovery, subnet scan, port scan, or listener.
- The application does not change firewall, Defender, Group Policy, PowerShell, SMB, services, drivers, or endpoint-protection settings.
- Robocopy and USMT are detected but never executed in Phase 1. USMT binaries are not redistributed.
- Fixed disks are not assumed to be internal or external: physical-disk evidence is used, and uncertainty remains `Unknown`.
- Registered Windows profiles and shell-folder mappings replace blind `C:\Users` enumeration and path guessing.
- Transfer state and verification state are independent. Strong verification means future SHA-256 comparison of source and destination; Phase 1 does not claim it occurred.
- There is **zero network telemetry**, no analytics, no crash upload, and no cloud service dependency.

See the focused [threat model](docs/security/Threat-Model.md) and [Windows validation plan](docs/validation/Windows-Validation-Plan.md).

## Phase 1 capabilities

- Read-only OS, architecture, machine, user-context, elevation, registered-profile, known-folder, logical-volume, and associated physical-disk analysis.
- USB/fixed/removable attachment classification using `Win32_LogicalDiskToPartition` and `Win32_DiskDriveToDiskPartition` evidence, with explicit uncertainty.
- System Robocopy version/path detection and installed Windows ADK ScanState/LoadState pair detection.
- Windows cloud-placeholder state abstraction and file-attribute classification for local, pinned, online-only, partial, unavailable, and unknown states.
- Versioned local JSON policy with strict parsing, semantic validation, safe UNC allow-listing, explicit tool/route permissions, verification level, and preservation-first conflict policy.
- Deterministic planner ordered by policy → capability → capacity → strategy, with technician-readable reasons.
- Atomic, schema-versioned JSON session journals and incomplete-session discovery behind `IMigrationJournal`.
- Streaming-oriented manifest and transfer contracts designed for large migrations without implementing file transfer.
- Avalonia MVVM technician shell with guided workflow, centralized design tokens, endpoint status, policy/error/loading/empty states, profile resolution confidence, and a clearly disabled future preparation action.

## Planner rules

1. Policy authorization is evaluated first and cannot be overridden by detected hardware or tools.
2. A network route is eligible only when enabled, explicitly allow-listed as an absolute UNC path, and accessible using the current identity.
3. External media is eligible only when policy permits it, Windows evidence classifies attachment as external, the volume is ready, and confirmed capacity meets the estimate.
4. An approved reachable share is preferred over eligible external media; this is deterministic, not a performance guess.
5. USMT is preferred when installed and allowed; otherwise Robocopy Known Folders is selected when installed and allowed. Route and strategy remain independent.
6. Unknown/online-only cloud state can block preparation for technician review.
7. No eligible route or strategy produces a blocked plan requiring manual action.

## Solution structure

| Project | Responsibility |
|---|---|
| `RoboTransfer.Core` | Platform-neutral domain, policy, error taxonomy, planning and execution/recovery contracts |
| `RoboTransfer.Windows` | Read-only storage, registered-profile, known-folder, cloud, elevation and approved-share detection |
| `RoboTransfer.Robocopy` | System Robocopy capability adapter only |
| `RoboTransfer.Usmt` | Existing Windows ADK USMT capability adapter only |
| `RoboTransfer.Persistence` | Strict policy loading and atomic local JSON session journal |
| `RoboTransfer.App` | Avalonia 12 MVVM technician experience and dependency composition |
| `RoboTransfer.Core.Tests` | Policy, planner, capacity and journal behavior |
| `RoboTransfer.Windows.Tests` | Storage classification, profile/folder classification, share boundary and cancellation behavior |

Important decisions are recorded in [`docs/architecture`](docs/architecture), with the concrete production path in the [engineering roadmap](docs/architecture/Roadmap.md).

## Build and run

Requirements: .NET 10 SDK. Runtime validation requires managed Windows 11 x64 hardware.

```powershell
dotnet restore RoboTransfer.sln
dotnet build RoboTransfer.sln -c Release --no-restore
dotnet test RoboTransfer.sln -c Release --no-build
dotnet run --project src/RoboTransfer.App/RoboTransfer.App.csproj
```

For publishing readiness, the project carries explicit product/version metadata and remains `asInvoker`. A future validated packaging pipeline may use:

```powershell
dotnet publish src/RoboTransfer.App/RoboTransfer.App.csproj -c Release -r win-x64 --self-contained true
```

This command is direction only, not a claim that an MSI, signed binary, single-file distribution, or corporate deployment validation currently exists.

## Local policy

Copy [`config/policy.example.json`](config/policy.example.json) to `%LOCALAPPDATA%\RoboTransfer\policy.json`, then have the enterprise policy owner explicitly enable only approved mechanisms. The shipped example and application default deny all routes and tools. Unsupported versions, invalid JSON, unsafe conflict defaults, and invalid UNC entries remain conservative and are surfaced in the UI; they never fall back permissively.

## Intentionally not implemented

There is no file scan, copy, deletion, Robocopy/USMT execution, hydration, remote pairing, transfer listener, verification hash, report generation, or installer in Phase 1. The disabled UI action does not imply readiness. Phase 2 scope is defined precisely in the [roadmap](docs/architecture/Roadmap.md); it must retain copy-only defaults, reject destructive mirroring, and require immutable manifest review plus explicit technician confirmation.
