# ADR-002: Robocopy is an execution adapter

**Status:** Accepted

Robocopy is detected separately and will implement `ITransferEngine`; it does not own planning or UI behavior. Future execution will use direct process argument lists, bounded retries, restartable copies where approved, cancellation, parsed exit codes, and progress. Destructive mirroring and implicit destination deletion are prohibited. This keeps route, strategy, policy, verification, and conflict handling testable without a Windows process.
