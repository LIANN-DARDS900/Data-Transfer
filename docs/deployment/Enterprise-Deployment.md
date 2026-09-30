# Enterprise deployment

## Release architecture

The preferred enterprise output is **framework-dependent `win-x64`**. A centrally serviced .NET 10 Desktop Runtime gives endpoint management one runtime inventory and security-servicing path, produces the smaller application package, and generally gives stable allowlisting hashes for application assemblies. It requires the approved x64 runtime before deployment. The optional self-contained profile improves isolated-endpoint predictability and removes that prerequisite, but materially increases package/patch size and makes each application package responsible for runtime vulnerability servicing. Both are non-single-file, deterministic builds for inspectability and allowlisting. Cold-start differences must be measured on target hardware rather than assumed.

Run `build/Release.ps1 -Profile FrameworkDependent -SourceRevisionId <commit>` (preferred) or `build/Release.ps1 -Profile SelfContained`. Outputs are under `artifacts/publish`. The MSI packages the framework-dependent output and installs x64 per-machine to `%ProgramFiles%\RoboTransfer`; it installs no service, driver, task, listener, firewall/Defender/SMB change, environment variable, or user-data cleanup action.

## Managed MSI commands

* Silent install: `msiexec /i RoboTransfer-0.4.0-win-x64.msi /qn /norestart /L*v C:\Windows\Temp\RoboTransfer-install.log`
* Upgrade: use the same command with a newer MSI. MajorUpgrade removes the older product after initialization and rejects downgrade.
* Silent uninstall: `msiexec /x {PRODUCT-CODE-FROM-INSTALLED-MSI} /qn /norestart /L*v C:\Windows\Temp\RoboTransfer-uninstall.log`

Use `Get-CimInstance Win32_Product` only where organizational policy accepts its MSI consistency-check side effects; otherwise detect the ARP entry under `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall`. Normal success is 0; 1641 means restart initiated (not requested here), 3010 means success/restart required, 1603 is fatal failure, and 1618 means another installation is active. MECM/SCCM should use the MSI product code/version detection populated from the built MSI. Uninstall preserves `%LOCALAPPDATA%\RoboTransfer` evidence.

## Signing readiness

Unsigned CI output is a **qualification build** with publisher `RoboTransfer Qualification Publisher`; it is not trusted production software. `build/Sign-Artifacts.ps1` accepts only a certificate-store thumbprint and HTTPS RFC 3161 timestamp URL, signs selected EXE/DLL/MSI artifacts with SHA-256, then runs `signtool verify /pa /all /v`. Production CI must inject access to an enterprise code-signing key through its secret-backed signing service, validate chain/revocation and timestamp, sign binaries before MSI packaging, and sign the MSI last. No key, certificate, password, timestamp service, or production publisher identity is stored here.

## Dependencies and servicing

Direct application dependencies are Avalonia Desktop/Fluent and Microsoft dependency-injection/logging packages. WiX Toolset 5 is build-time only. The framework-dependent deployment relies on the Microsoft .NET 10 Desktop Runtime x64. Release validation retains NuGet audit for all dependency levels and does not suppress NU1901–NU1904. Review `dotnet list RoboTransfer.sln package --include-transitive` at every RC and update intentionally.
