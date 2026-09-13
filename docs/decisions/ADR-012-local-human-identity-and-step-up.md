# ADR-012: Local Human Identity, Roles and Step-Up

**Status:** Accepted with Phase 2 implementation gate

## Context

Pairing authenticates an Android device to one Local Connector; it does not identify the human using that device. Pharma Auto nevertheless assigns different authority to an Operator, Catalog Manager and Pharmacy Supervisor, and its audit contract requires the real actor for corrections, approvals, New Item creation and future Genius writes. A device label, shared pharmacy password or caller-supplied display name cannot satisfy that requirement.

Local review and future supervised writes must continue during an allowed Internet outage. SaaS administrator identities are also a separate trust domain and must not become local pharmacy identities or grant Genius authority.

## Decision

### Local identity authority

- The Local Connector is the authority for pharmacy-human identities used on its local workflow. Each person has a stable opaque `actorId`, display label, status, credential version and role assignments scoped to the tenant, pharmacy and Connector installation.
- Device identity and human identity are independent. Pairing grants a device the ability to reach the Local API; it grants no Operator, Catalog Manager or Supervisor role.
- SaaS may receive bounded actor-reference metadata for audit correlation, but it cannot create a local role, authenticate a pharmacy user for an offline command or approve a Genius write.
- Shared permanent user credentials and universal support passwords are prohibited. Credentials are verified by the Connector using a reviewed password/passphrase or phishing-resistant credential profile. Password verifiers use a reviewed memory-hard KDF،unique salt and versioned parameters؛plaintext،reversible encryption and unsalted/fast hashes are prohibited. Short PIN-only authentication is prohibited for privileged roles; every verifier, recovery secret and signing key is protected at rest under the Connector machine boundary.

### Provisioning and role changes

- Installation creates a single-use, time-limited enrollment ceremony for the first Pharmacy Supervisor. It records pharmacy-owner authorization and does not make the Local Technician a pharmacy user.
- An active Supervisor may provision or disable Operators and Catalog Managers. Granting, removing or recovering a Supervisor role requires a second active Supervisor when one exists; otherwise it uses the documented pharmacy-owner recovery ceremony and places all write capabilities in a locally visible hold until the recovery audit is acknowledged.
- Role assignment is deny-by-default and versioned. At minimum, the roles are `OPERATOR`, `CATALOG_MANAGER` and `SUPERVISOR`; permissions such as `CATALOG_CREATE`, `INVOICE_CONFIRM`, `GENIUS_COMMIT`, `EXPIRY_OVERRIDE` and `DUPLICATE_OVERRIDE` are evaluated server-side from the current role-policy version.
- Disabling a user or changing a role increments its credential/role version and immediately invalidates that user's local sessions and outstanding approvals.

### Sessions

- The Connector issues an Android business session only after both the paired device and human credential are verified. The session is device-bound and actor-bound and contains opaque IDs for the actor, device and session plus authentication time and role-policy version؛Android cannot supply or overwrite the effective actor or roles. First-Supervisor/recovery actions in the Windows Control UI use a separate protected local-console session bound to the host،not a fabricated Android device identity.
- A normal local session has a maximum eight-hour absolute lifetime and a fifteen-minute idle timeout. A deployment may shorten these limits, never lengthen them without a reviewed policy revision.
- Every business command rechecks the current device, user, session and role state. Transport tokens alone never authorize invoice viewing, correction, confirmation, catalog creation or a write.
- Stored capture transport may resume under device identity, but viewing tenant invoice content and issuing review or business commands require a valid human session. Internet loss does not extend a session and does not bypass local authentication.

### Online authentication abuse controls

- Every credential and step-up attempt is throttled server-side by the Local Connector. Limits are keyed at minimum by the `(actorId, deviceId)` pair and are reinforced by independent per-actor, per-device and source-network buckets so rotating any one identifier cannot bypass the control.
- Repeated failures trigger progressive, bounded backoff followed by a policy-defined temporary lockout. The Connector persists the relevant counters across service restarts, records security audit events without credential material and does not let a successful attempt immediately erase evidence needed to detect a distributed guessing campaign.
- Authentication, enrollment and recovery responses are enumeration-resistant: unknown, disabled and temporarily locked actors receive the same public response shape and materially equivalent work/timing as an invalid credential. Android does not learn whether an actor identifier exists from status codes, response text or retry metadata.
- Lockout recovery never becomes a weaker authentication path. It requires the same second-Supervisor or documented pharmacy-owner ceremony as credential recovery, revokes affected sessions and approvals, rotates the credential version and preserves the write hold and audit requirements described above.

### Step-up and approval binding

- High-impact actions require fresh authentication no more than five minutes old. The minimum step-up set is New Item creation, Genius Commit, missing-expiry override, ambiguous-duplicate override, privileged selling-price impact and user/role administration.
- Step-up approval is bound to the exact action, `jobId`, immutable revision or command ID, impact hash, initiating actor, approving actor, device and expiry. It is single-use and cannot authorize a changed revision.
- Current device،actor،session،role-policy version and step-up binding are checked again immediately before a Genius transaction begins. Queueing or confirmation does not reserve human authority after session،role or approval expiry.
- When an Operator or Catalog Manager initiates a Supervisor-only action, the Supervisor authenticates separately. Audit records preserve both initiator and approver. A policy that requires four-eyes approval also requires different actors; a Supervisor may not self-approve that policy merely by repeating a credential.
- A Supervisor acting within a policy that permits self-authorization must still perform fresh re-authentication, and the audit records that no second actor was required by that policy version.

### Actor audit

- The Connector derives the actor from the authenticated principal. It records `actorId`, actor type, session ID, device ID, role and policy versions, authentication assurance/time, action, target, immutable revision/command ID, result and correlation ID.
- Approval events also record initiator, approver, reason, impact hash and whether four-eyes policy applied. Display names are snapshots for readability and are never audit identity keys.
- User enrollment, credential recovery, role changes, authentication failures, session revocation and step-up decisions are security audit events. Raw credentials and recovery secrets never enter audit or application logs.
- If actor identity, current authorization or approval binding cannot be proven, the command fails closed. Support may help recover an account but cannot impersonate a pharmacy actor or mint an approval.

## Consequences

- Phase 1 remains read-only،and its human authorization acceptance remains open until these contracts and tests exist.
- Accepting this decision does not enable a write. Phase 2 cannot expose New Item mutation commands until provisioning،session revocation،permission،transaction-time recheck،step-up،online-guessing throttling/backoff،anti-enumeration and lockout/recovery tests pass with independent Security/Product approval.
- Android needs an explicit sign-in/lock/actor-switch experience suitable for a shared counter device.
- Local recovery is more deliberate،but the implemented boundary will stop audit attribution from collapsing a person into a paired device or shared pharmacy credential.
- Manual e-plus entry remains the operational path when local identity recovery places Pharma Auto writes on hold.
