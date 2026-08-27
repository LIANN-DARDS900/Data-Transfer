# RoboTransfer threat model

**Scope:** Phase 1 local endpoint analysis, policy evaluation, planning, and session persistence. Transfer execution is not implemented.

## Assets and trust boundaries

Protected assets are employee file content and metadata, Windows credentials/tokens, approved destination paths, enterprise policy, migration journals, and the integrity of technician-visible results. Trust boundaries exist between the local process and: Windows profile/registry data; filesystem and reparse points; removable media; approved UNC storage; external executables (Robocopy and USMT); and local policy/journal files. RoboTransfer treats capability presence as untrusted evidence until policy authorizes its use.

## Concrete threats and controls

| Threat | Consequence | Phase 1 control | Required before execution |
|---|---|---|---|
| Policy file tampering or malformed configuration | Unauthorized route or weakened verification | Versioned strict parsing, semantic validation, fail-closed conservative policy | Protect configuration with enterprise ACLs; record policy digest in session |
| Crafted UNC path | Connection to unauthorized infrastructure | Only explicitly listed absolute UNC paths are checked; no discovery | Allow-list normalization and administrator-managed file ACLs |
| Malicious junction, symlink, mount point, or reparse target | Data escape, loops, unintended disclosure | No manifest traversal exists in Phase 1 | Never follow reparse directories by default; enforce source volume/root boundary |
| Untrusted removable media | Malware, destination substitution, data exposure | Read-only detection; uncertain attachment is not eligible | Device identity revalidation, encryption policy, free-space reservation, destination ACL checks |
| Local attacker edits a journal | False resume state or destination redirection | Schema and session/file identity validation; atomic replacement | Authenticated journal envelope or ACL-protected SQLite; revalidate plan before resume |
| Online-only cloud placeholder | False success or incomplete migration | Placeholder states modeled independently; uncertainty can block readiness | Supported Cloud Files classification, explicit hydration/skip policy, manifest reporting |
| Tool path replacement | Arbitrary executable execution | Detection only; system/ADK paths; nothing executed | Validate canonical path, signature/publisher, version, and argument allow-list |
| Destination conflict | Existing employee data overwritten | Preserving `KeepBoth` default; destructive default policy rejected | Explicit per-session conflict confirmation and immutable audit record |
| Log disclosure | User/path information leakage | Structured aggregate logs avoid full profile/file paths | Redaction rules and technician-controlled diagnostics export |
| False verification claim | Undetected corruption | Transfer and verification states are independent | Standard checks and SHA-256 source/destination hashing implemented independently |

## Enterprise policy boundary

RoboTransfer does not change firewall, Defender, Group Policy, execution policy, SMB configuration, network discovery, services, or endpoint controls. A denied route remains ineligible even when technically available. Phase 1 makes no outbound connection except an accessibility check for a policy-listed UNC path. There is zero telemetry.

## Residual risk

WMI and registry metadata can be missing, stale, or access-denied; the product reports uncertainty rather than inferring attachment or profile authority. `Directory.Exists` on an approved UNC can be delayed by the OS provider and cannot guarantee future write access. Physical Windows testing and time-bounded access validation are release gates.
