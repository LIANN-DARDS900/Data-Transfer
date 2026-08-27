# RoboTransfer

**Enterprise-safe PC migration orchestration for moving an employee from an OLD Windows PC to a NEW Windows PC.** RoboTransfer analyzes the local environment, separates technical availability from organizational authorization, and recommends an approved transport and migration strategy. It is not a general-purpose Robocopy GUI.

> **RoboTransfer does not bypass enterprise network or endpoint security controls. It operates only through migration mechanisms explicitly allowed by policy/configuration.**

## Phase 1

Phase 1 provides a non-destructive foundation:

- read-only OS, elevation, storage-volume, removable-volume, and selectable local-profile analysis;
- detection of the system Robocopy executable and its file version without running it;
- detection of an existing Windows ADK USMT installation (both ScanState and LoadState), without downloading or running it;
- accessibility checks only for explicitly approved UNC paths—there is no discovery, subnet scan, or port scan;
- a conservative, strongly typed `PolicyProfile` that denies every migration mechanism by default;
- deterministic planning that keeps the transport route separate from the migration tool;
- local JSON session-journal storage behind the platform-neutral `IMigrationJournal` contract;
- manifest records with transfer, verification, known-folder, and cloud-placeholder state. Placeholder detection remains intentionally unimplemented and defaults to `Unknown`;
- an Avalonia MVVM dashboard for analysis, recommendations, device role, and OLD-PC profile selection.

No copying, deletion, remote pairing, custom networking, listener, firewall change, service installation, policy modification, USMT execution, or Robocopy execution exists in Phase 1.

## Planning rules

Rules are deterministic and apply in this order:

1. Policy gates every route and tool. Technical presence never overrides a denial.
2. A configured network route is eligible only when network shares are permitted, its UNC path is explicitly listed, and that exact path is accessible.
3. External storage is eligible only when permitted, detected as removable, ready, and large enough for a supplied estimate.
4. An eligible configured share is selected before removable storage; neither causes security configuration changes.
5. USMT is preferred when detected and permitted; otherwise Robocopy Known Folders is used when detected and permitted.
6. With no eligible route or tool, the result is blocked and requires manual approval.

Every plan carries structured reasons for selections and rejections.

## Architecture

| Project | Responsibility |
|---|---|
| `RoboTransfer.Core` | Platform-neutral immutable domain models, interfaces, policy, and planner |
| `RoboTransfer.Windows` | Read-only OS, profile, volume, elevation, and approved-share detection |
| `RoboTransfer.Robocopy` | Robocopy presence/version detection only |
| `RoboTransfer.Usmt` | Installed Windows ADK USMT detection only |
| `RoboTransfer.Persistence` | Atomic local JSON session journal |
| `RoboTransfer.App` | Avalonia 12 MVVM UI and dependency-composition root |
| `RoboTransfer.Core.Tests` | Planner decision matrix and conservative-policy tests |
| `RoboTransfer.Windows.Tests` | Approved-path policy-boundary tests |

Core has no Windows dependency. Windows behavior is behind Core interfaces, and the application runs as the current user (`asInvoker`) rather than demanding elevation.

## Requirements and build

- .NET 10 SDK
- Windows 11 x64 for real capability/runtime validation

```powershell
dotnet restore RoboTransfer.sln
dotnet build RoboTransfer.sln -c Release --no-restore
dotnet test RoboTransfer.sln -c Release --no-build
dotnet run --project src/RoboTransfer.App/RoboTransfer.App.csproj
```

The solution can be restored and compiled on other .NET-supported hosts. OS-specific detectors report normal unavailable/unknown states outside Windows; final device, elevation, ADK, UNC authorization, and removable-media behavior must be verified on a policy-managed Windows 11 endpoint.

## Configuration direction

`PolicyProfile.Conservative` is the current application default. Approved UNC paths and permissions must eventually be supplied by technician-managed configuration and never inferred. Phase 2 should introduce validated policy-file loading and schema/version handling before enabling any transfer engine.

## Intentionally deferred

Phase 1 does not transfer data. Phase 2 should add manifest estimation and reliable Windows cloud-placeholder classification first, with cancellation, reparse-point safety, inaccessible-item warnings, capacity reservation, signed-plan confirmation, and strong post-copy verification. Execution should remain opt-in and restricted to an approved route and tool; remote discovery and a custom transfer protocol should remain out of scope.
