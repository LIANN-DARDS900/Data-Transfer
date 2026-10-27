# ADR-006: Receiver inboxes on an existing approved Windows share

**Status:** Implemented for qualification; managed-device/RJ45 qualification pending.

## Decision

Add a receiver/sender handoff over the approved UNC transport already defined by ADR-003. The new PC prepares a unique inbox below an existing approved share **hosted by that PC**. The old PC checks its immutable descriptor, displays the receiving PC and employee, requires the operator to compare an eight-character inbox identifier and confirm the recipient, then locks the receiver binding into its execution plan.

This is an SMB workflow, not a custom peer-to-peer protocol. No discovery, listener, port scan, share creation, firewall rule, network-adapter reconfiguration, credential collection or security-policy change is introduced. An RJ45 cable provides the physical link; addressing/name resolution, Windows SMB access and an approved share must already work. If SMB is blocked, this route is blocked. The application does not switch to another port or tunnel.

The code is a visible identifier, **not** a password or cryptographic pairing secret. Windows authenticates and authorizes filesystem access. A permitted technician must confirm the receiving employee on both screens. Approved shares must have suitable permissions restricting metadata and payload writes to trusted identities. Share administrators remain part of the trust boundary.

## Inbox layout

```text
<approved UNC root>/RoboTransferInbox/<random GUID>/
    receiver.json       immutable, versioned receiver descriptor
    sender.json         exclusive migration claim
    status.json         atomically replaced sender progress
    closed.json         only for closing an unclaimed inbox
    Files/<KnownFolder>/...
    report.json
    report.html
```

Paths in production use Windows separators. Receiver metadata is bounded to 64 KiB, parsed with a strict schema, and checked for path/schema/session/recipient consistency. Device paths, relative traversal, alternate streams and reparse components are rejected. The policy is checked **before** accessing an input inbox path. The receiver descriptor is fingerprinted and revalidated before claim, copy, verification, report publication and resume.

An unclaimed inbox expires after 12 hours. A claimed inbox stays available to the same migration session on the same source machine for validated completion/resume; expiry does not strand an existing job. Atomic create-without-overwrite prevents competing sender sessions from owning the same inbox. A claimed inbox cannot be closed through the receiver UI. Finishing monitoring preserves its files and reports.

Free space must be known and sufficient before claiming/starting a transfer. The execution plan is persisted before its remote claim so recovery retains the binding if the process stops after claiming. Plans without a receiver omit the new optional field, retaining their previous serialization/fingerprint shape.

## Actual file destination and evidence

The selected employee labels this migration. Incoming files are written into the inbox's `Files` folder, not merged into an existing Windows profile or redirected Known Folders. The existing preservation-first copy engine, source/destination SHA-256 verification and report generation are reused. Profile settings/application migration remains separate future USMT work.

A bounded progress publisher drains before the final status is written. The receiver polls every two seconds while its screen is open and labels updates as **reported by the old PC**. It never treats a copied byte count as verification or performs an independent source-vs-destination verification of its own. Final sender-generated JSON/HTML reports are published into the same authorized inbox; failed publication is surfaced to the sender.

Control-file atomic replacement prevents partial JSON from being read. It does not remove filesystem/SMB provider delays or malicious-administrator/TOCTOU risks; share permissions and endpoint controls remain required. Cancellation of a Windows filesystem request may wait for the OS network provider timeout.

## Qualification boundary

Automated tests map a virtual UNC share into isolated local directories. They cover the full claim/copy/strong-verification/report path with actual fixture bytes, policy-before-access, strict path matching, changed identity, capacity, concurrent claim, expiry/resume, wrong source machine, malformed/oversized metadata and reparse rejection. They do **not** prove a real Ethernet link, SMB authentication, endpoint allowlisting or cable interruption behavior.

Physical qualification must use two managed Windows PCs, approved computer-name/FQDN UNC roots, real profile data and the existing qualification matrix. IP-address/alias receiver roots are not enabled in this build; they require a separately verified endpoint-identity design. No blocked route is classified as working.
