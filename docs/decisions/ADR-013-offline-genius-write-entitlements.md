# ADR-013: Offline Genius Write Entitlements

**Status:** Accepted with Phase 2 implementation gate

## Context

SaaS is the subscription authority, while the Local Connector is the only component allowed to execute a future certified Genius write. Capture and stored review must tolerate Internet outages, and the architecture previously allowed an offline Commit while a cached signed entitlement remained valid. An OCR entitlement, an authenticated Connector and a confirmed invoice do not by themselves authorize a database mutation.

Without a capability-specific lifetime, trusted-time rule and revocation semantics, a copied entitlement, clock rollback or stale confirmed revision could extend write authority after subscription expiry or a security incident.

## Decision

### Explicit write capabilities

- SaaS issues signed, capability-specific write grants. OCR/page quota never implies a Genius write capability.
- `GENIUS_PURCHASE_COMMIT` and `GENIUS_MASTER_ITEM_CREATE` are separate capabilities. Future write types require their own named capability and policy review.
- A grant is bound to tenant, pharmacy, Connector installation, Genius profile, approved database fingerprint, capability, policy version, unique grant ID, revocation generation, capability-scoped monotonic issuance sequence, `issuedAt`, `notBefore` and `expiresAt`.
- A grant authorizes only the Connector installation and profile named in it. It is not human authorization: the current local role, step-up and approval requirements from ADR-012 must also pass.

### Lifetime and caching

- The production default lifetime is eight hours and the hard maximum is twenty-four hours from signed issuance. A plan or deployment may shorten the lifetime but cannot exceed the hard maximum without a superseding ADR and threat review.
- Cached grants are encrypted and integrity-protected under the Connector machine boundary. They are never copied to Android, logs, diagnostics or the Genius database.
- Offline state cannot refresh, extend or replace a grant. A new grant requires an authenticated online exchange with SaaS.
- Offline write eligibility is limited to the same continuous Connector service/OS run that completed the authenticated grant exchange. A service restart،OS reboot or loss/ambiguity of protected time state requires online revalidation before another Genius transaction؛capture،stored review and reconciliation remain available.
- The Connector validates the grant when a write command is queued and again immediately before opening the Genius transaction. A valid grant at confirmation time does not reserve future write authority.

### Expiry and in-flight work

- If a grant expires after invoice confirmation but before the database transaction begins, the revision remains confirmed and read-only; the write stays blocked until a new valid grant is obtained and all preflight checks are rerun.
- Once a transaction has begun under a valid grant, expiry alone does not interrupt or roll back that transaction. The Connector must finish outcome classification and reconciliation. Expiry never permits a retry of `CommitUnknown`.
- Catalog creation and Purchase Commit validate their own capability independently even when both are associated with one invoice.

### Revocation and generation

- SaaS maintains a Connector revocation generation. Each grant carries the current generation and an issuance sequence monotonic within `(Connector, capability)`. The Connector persists the highest valid values it has observed. A lower value is rejected؛an equal sequence is accepted only for the byte-identical grant ID/payload،while an equal sequence with different content is substitution and fails closed.
- On every SaaS contact،an authoritative revocation،certificate rejection or subscription denial invalidates the corresponding cached grants immediately. A successful response advances trusted generation/time state when newer؛a locally revoked device or user blocks its commands independently of grant validity.
- Revocation cannot be delivered instantly to a fully offline compromised host. The hard grant lifetime is the maximum remote-revocation exposure; this residual risk is explicit and cannot be described as immediate revocation.
- A local Supervisor may activate a visible write hold that invalidates cached grants on that Connector. Clearing a security hold requires online revalidation plus audited Supervisor step-up.

### Trusted time and rollback detection

- Each online grant exchange records signed server time،grant generation/sequence،a monotonic elapsed-time anchor and the highest observed UTC in integrity-protected state.
- During that continuous service/OS run،the authorization clock is signed server time plus monotonic elapsed time. Wall clock is only a rollback/drift signal and never extends `expiresAt`.
- A backward wall-clock movement greater than five minutes،a missing/invalid protected clock record،grant-sequence regression،service/OS restart or restoration of older entitlement state disables all Genius writes. Only a successful online trusted-time and grant refresh restores eligibility.
- Clock uncertainty never extends `expiresAt`. Capture, queue inspection and stored review may continue while writes are time-blocked.

### Emergency behavior

- There is no offline break-glass token, support override, hidden grace flag or local method to mint/extend a write grant.
- When Internet access, entitlement, trusted time or revocation state blocks a write, the system preserves the confirmed revision and explains the gate. The authorized emergency path is manual entry through e-plus.
- Reconciliation and read-only investigation of a transaction that may already have committed remain available after grant expiry or revocation; new mutation and automatic retry do not.

## Consequences

- Offline pharmacy work remains useful for capture and review, but write availability is deliberately bounded.
- Remote revocation during total disconnection is bounded rather than instantaneous and must be accepted as residual risk before Pilot.
- Accepting this decision does not enable a write. Phase 2 cannot implement a clone writer until the capability schema،continuous-run trusted-time state،replay/rollback/restart tests and ADR-012 human authorization gate pass with independent Security/Operations approval.
- A service/OS restart during an Internet outage sacrifices automated write availability until reconnection؛this is the deliberate cost of not trusting cached wall-clock state across restarts.
- Operations needs alerts for expired grants, rollback detection, generation regression and local write holds without logging the signed grant itself.
- Manual e-plus entry remains available when subscription or security policy prevents Pharma Auto from writing.
