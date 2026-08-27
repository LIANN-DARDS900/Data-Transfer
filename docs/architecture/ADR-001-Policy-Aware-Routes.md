# ADR-001: Policy gates routes before capabilities

**Status:** Accepted

RoboTransfer evaluates explicit local policy before capability, capacity, and strategy. A technically reachable share or attached drive is never eligible unless its route is allowed. Invalid, missing, or unsupported policy loads the conservative deny-by-default profile and surfaces errors.

This sacrifices automatic convenience but prevents environmental detection from becoming authorization. Policy is versioned and validated offline; no remote policy service is introduced.
