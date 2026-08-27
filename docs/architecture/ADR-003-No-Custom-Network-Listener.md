# ADR-003: No custom network listener

**Status:** Accepted

RoboTransfer will not implement peer discovery, a custom TCP protocol, inbound listener, port scan, or firewall automation. Network destinations come only from validated policy-listed UNC paths. This respects endpoint controls and avoids adding an unaudited transport/security boundary. Enterprises may use approved shares or removable media; unavailable routes are explained rather than bypassed.
