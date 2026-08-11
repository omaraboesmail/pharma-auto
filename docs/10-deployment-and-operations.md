# Deployment and Operations

## 1. Environments

- **Local Lab:** restored Genius backup وe-plus test workstation.
- **Development:** fake/synthetic documents،لا production DB.
- **Staging:** cloud stack مستقل وConnector متصل بـ Clone DB.
- **Pilot:** tenant حقيقي واحد،supervised commits.
- **Production:** staged tenant rollout.

لا تستخدم production backup في developer laptops دون encryption،access control وretention.

## 2. Connector Installation

Installer يقوم بـ:

- prerequisite checks: supported Windows،disk،.NET runtime،Defender status.
- إنشاء Windows Service account محدود.
- install signed binaries.
- إنشاء Sidecar وencrypted data directories.
- certificate enrollment.
- SQL connectivity/read-only fingerprint test.
- explicit write permission test دون تنفيذ business write.
- Android pairing initialization.
- backup/diagnostic path configuration.

Control UI تكون visible لعرض health والـ queue، لكن service تستمر بعد إغلاقها.

## 3. Connector Upgrade

- signed release manifest.
- staged rings: lab → internal → pilot → limited production → general.
- DB profile compatibility checked قبل activation.
- rollback إلى previous binary دون downgrade Sidecar destructive migration.
- jobs في `Committing` تمنع upgrade حتى resolution.
- security revocation يمكنها إيقاف version قديمة دون حذف local data.

## 4. SaaS Deployment

- immutable container images.
- database migrations backward-compatible خلال rollout.
- deploy API/workers independently داخل نفس release contract.
- object lifecycle policies managed as infrastructure.
- secret rotation دون image rebuild.
- production access عبر audited identity،لا shared SSH credentials.

## 5. Backups

### Sidecar

- encrypted daily backup.
- retention حسب pharmacy policy.
- restore test ربع سنوي.
- backup لا يشمل expired raw invoice files بعد TTL.

### SaaS PostgreSQL

- managed point-in-time recovery.
- encrypted backups.
- periodic restore إلى isolated environment.
- tenant/audit integrity validation بعد restore.

Genius backup يظل مسؤولية الصيدلية/e-plus، لكن Connector يرفض Commit إذا backup/health policy المطلوبة لم تتحقق وفق configuration.

## 6. Monitoring

### Connector Metrics

- online/version/fingerprint status.
- catalog sync age.
- queue depth and oldest job.
- temp storage size/deletion failures.
- DB commit duration/lock waits.
- reconciliation outcomes.
- `CommitUnknown` count.

### SaaS Metrics

- OCR latency/failure/cost.
- quota reservation leaks.
- tenant request rates.
- object deletion backlog.
- certificate/auth failures.
- admin privileged actions.

### Alerts

Critical:

- reconciliation mismatch.
- repeated `CommitUnknown`.
- tenant isolation/security event.
- expired/revoked Connector still attempting writes.
- deletion lifecycle failure.
- DB fingerprint drift.

## 7. Runbooks

مطلوب قبل Production:

- Connector offline.
- SQL connection/TLS failure.
- fingerprint mismatch.
- catalog sync corruption/rebuild.
- OCR provider outage.
- quota settlement stuck.
- `CommitUnknown` investigation.
- reconciliation mismatch.
- certificate compromise/rotation.
- admin account compromise.
- temp file deletion failure.
- Sidecar restore.

## 8. Incident Rules

- أي suspected silent corruption يوقف Direct Commit للـ tenant فورًا ويترك capture/review متاحين.
- لا يتم تعديل Genius يدويًا من support دون pharmacy owner وإجراء e-plus معتمد.
- evidence bundle redacted ويحتوي hashes،states وDB postconditions،لا raw secrets.
- incident review ينتج ADR أوtest جديدًا إذا كشف assumption معمارية.

## 9. Capacity and Cost

التكلفة تُقاس حسب:

- pages per invoice.
- OCR input/output usage.
- temporary object bytes/time.
- support minutes لكل 100 invoice.
- correction rate.
- Connector install/upgrade effort.

بيع subscription على request count فقط دون page/processing cost سيخلق plans خاسرة.
