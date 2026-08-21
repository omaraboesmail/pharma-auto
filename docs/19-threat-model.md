# Threat Model

Status: **Accepted Phase 0 threat inventory; Phase 1 controls implemented locally, with independent production verification still open**

Baseline date: 2026-08-21  
Method: asset and trust-boundary review with STRIDE-style threat enumeration.  
Scope: Android client, pharmacy LAN, Local Connector, Genius, SaaS, Gemini, object storage, PostgreSQL/pgvector and Admin Portal.

## 1. Security Objectives

1. Prevent unauthorized or ambiguous Genius writes and detect every stock/financial mismatch.
2. Keep SQL credentials, Connector private keys and Gemini credentials in their authorized trust zones.
3. Preserve tenant separation and prevent OCR/mapping evidence from choosing final local Genius identities.
4. Protect invoice content during temporary processing and delete it under an auditable TTL.
5. Preserve source text and evidence without prompt injection, BiDi spoofing or heuristic repair changing business identity.

## 2. Assets and Classification

| Asset | Classification | Authority |
|---|---|---|
| SQL write credentials and Connector private key | Restricted secret | Local Connector / Windows security boundary |
| Gemini credential | Restricted secret | SaaS KMS/Secret Manager |
| Invoice pages and OCR text | Confidential tenant data | Pharmacy during capture; temporary SaaS processor under policy |
| Genius catalog, stock and financial state | Restricted business system | Pharmacy-local Genius database |
| Sidecar job, mapping, commit and audit state | Restricted operational data | Local Connector |
| Tenant, subscription, quota and cloud audit state | Confidential control-plane data | SaaS |
| Admin identities and break-glass evidence | Restricted security data | SaaS identity/security administration |
| Canonical Pharma catalog and embeddings | Internal shared reference | SaaS; never local Genius identity authority |

## 3. Actors and Trust Boundaries

Relevant actors are an authorized operator, pharmacy supervisor, local technician, SaaS administrator, external OCR provider, lost/compromised Android device, malicious LAN peer, compromised workstation, malicious tenant, compromised admin and software-supply-chain attacker.

| Boundary | Authenticated channel | Data allowed across it | Data forbidden across it |
|---|---|---|---|
| Android ↔ Connector | paired device identity + short-lived token over LAN TLS | pages, review commands, local opaque references, status | SQL/Gemini credentials, SQL identifiers as authority |
| Connector ↔ Genius | least-privilege local SQL account + `DBFP-1` | certified reads; later certified writes and reconciliation | SaaS-originated SQL, schema mutation, blind retry |
| Connector ↔ SaaS | installation certificate, mTLS and tenant authorization | encrypted document jobs, quota/OCR state, bounded telemetry | SQL credentials, full Genius catalog/stock, final `itm_id` authority |
| SaaS ↔ Gemini | centrally held credential over TLS | bounded invoice content and strict structured-output request | tenant secrets, SQL data, execution authority |
| Admin ↔ SaaS | verified JWT, server-enforced AAL2 and RBAC | tenant operations permitted by role | default raw invoice access, unrestricted cross-tenant data |

## 4. Threat Register

| ID | Threat and impact | Preventive/detective controls | Verification | Owner | Residual state |
|---|---|---|---|---|---|
| TM-01 | Lost or cloned Android device submits or views work. | Keystore key, one-time pairing, revocation, short tokens, no permanent shared token. | Pair/revoke/replay tests. | Security Reviewer | Controls implemented؛independent device security verification open. |
| TM-02 | LAN interception, spoofing or replay alters invoice review commands. | TLS, device identity, nonce/idempotency, revision preconditions. | MITM/replay and stale-revision tests. | Security Reviewer | Controls implemented؛MITM/replay verification remains production-blocking. |
| TM-03 | Connector certificate/private key theft enables cloud impersonation. | Windows protected key, non-exportable where supported, rotation and revocation. | Export attempt and revocation tests. | Security Reviewer | Open until deployment validation. |
| TM-04 | SQL credential theft or excessive grants enables corruption or unrelated reads. | DPAPI/machine protection, object-level grants, local firewall, no `sa`/`db_owner`. | Least-privilege and secret-scan tests. | Security Reviewer | Production-blocking. |
| TM-05 | Malicious invoice text performs prompt injection or causes unsafe matching. | Treat all OCR content as data, strict schema, no tool/SQL authority, human/local identity confirmation. | Adversarial fixture and schema-rejection tests. | SaaS OCR Owner | Strict local contract gate implemented؛adversarial provider validation open. |
| TM-06 | PDF/image bomb, parser exploit, path traversal or active content compromises processing. | Magic bytes, size/page/decompression limits, sandboxed parser, random object keys, malware scan. | Fuzz, bomb and traversal tests. | Security Reviewer | Local limits/Defender implemented؛fuzz and recovery verification open. |
| TM-07 | OCR provider or logs expose invoice data. | Minimal payload, encrypted temporary storage, no raw content in logs, explicit TTL/deletion verification. | Log inspection, retention and deletion tests. | Security Reviewer | Commercial/privacy terms require acceptance. |
| TM-08 | Cross-tenant query, quota or mapping leakage corrupts another tenant. | Tenant from principal, row-level enforcement, atomic quota, no cross-tenant auto-confirm. | Tenant-isolation and concurrency tests. | SaaS Security Owner | Production-blocking. |
| TM-09 | Admin takeover or support misuse exposes tenant data. | Server-enforced AAL2, scoped RBAC, time-bound break-glass and immutable audit. | Privilege, expiry and audit tests. | Security Reviewer | Production-blocking. |
| TM-10 | Retention job failure leaves invoices beyond policy. | Encrypted object TTL, verified deletion, backlog metrics/alert, hashes-only audit. | Deletion failure/recovery test. | SaaS Operations Owner | Open until retention duration is approved. |
| TM-11 | Incorrect reverse-engineered write rule silently corrupts stock or financial state. | Write-disabled profile, Golden matrix, `DBFP-1`, transaction and independent reconciliation. | Golden/fault suite with 100% reconciliation coverage. | DB Integration Owner | Production-blocking. |
| TM-12 | `CommitUnknown` is retried and duplicates a purchase. | Durable commit journal, fixed confirmed-revision key, no automatic retry, read-only investigation. | Connection-loss/power-loss duplicate tests. | DB Integration Owner | Production-blocking. |
| TM-13 | Concurrent e-plus and Connector activity defeats application locks. | DB locks, short transaction, consistent order, final duplicate check; no reliance on `sp_getapplock` alone. | Concurrent manual/Connector Golden scenarios. | DB Integration Owner | Production-blocking. |
| TM-14 | Compromised workstation alters Connector, Sidecar or evidence. | Least-privilege service, signed release, endpoint protection, encrypted storage and audit export. | Installer/signature/tamper tests. | Security Reviewer | Requires pharmacy compensating controls. |
| TM-15 | Corrupt/mixed-direction Genius labels spoof an Item identity. | Exact identifiers first, raw labels marked untrusted, BiDi isolation only at display, no heuristic repair. | `itm_id=60495`, mixed-script and 200% scaling regression tests. | Product Owner | Name-only auto-match prohibited. |
| TM-16 | OCR/quota abuse creates unbounded cost or denial of service. | Atomic reservation, page/file limits, tenant budgets and idempotent settlement. | Contention, retry and budget tests. | SaaS Operations Owner | Local Phase 1 baseline recorded؛production contention and budget validation open. |
| TM-17 | Dependency, build or signing compromise ships malicious clients/services. | Locked dependencies, CI validation, secret scanning, signed artifacts and controlled release roles. | SBOM/dependency/signature checks. | Security Reviewer | Open until release pipeline exists. |
| TM-18 | Legacy triggers cause hidden whole-stock snapshot, runtime DDL, audit or row-deletion effects when a seemingly narrow stock/financial row changes. | Trigger-definition fingerprint, required dependency tables, named data-invariant preflight, row-at-a-time writes and all-table Golden reconciliation. | Pending-month-close and destructive-branch Golden scenarios plus fault tests. | DB Integration Owner | Production-blocking. |

## 5. Misuse Cases That Must Fail Closed

- Android or SaaS supplies `pth_id`, `c_id`, SQL text or a final Genius identity.
- A schema/fingerprint mismatch is bypassed to “keep the pharmacy running.”
- An unknown commit outcome is retried automatically.
- Vector similarity overrides strength/form/pack conflicts or human/local resolution.
- Missing expiry, ambiguous duplicate or New Item is silently accepted.
- Raw invoice text is interpreted as an instruction to call tools, change prompts or query SQL.
- Admin access to raw tenant documents is granted without time-bound break-glass evidence.

## 6. Acceptance and Maintenance

Phase 0 accepts the threat inventory, trust boundaries, owners and planned verification. Phase 1 implements the read-only controls listed in [Phase 1 Closure](21-phase-1-closure.md) without accepting the remaining residual risks for production. The production gates in [Testing and Acceptance](09-testing-and-acceptance.md) remain mandatory.

Update this model when a trust boundary, credential location, external provider, stored data class, Genius write surface or privileged role changes. A material change requires an ADR and a new threat-model baseline date.
