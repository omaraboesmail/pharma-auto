# Security and Privacy Specification

The trust-boundary threat inventory،owners and residual-risk state are maintained in the [Phase 0 Threat Model](19-threat-model.md).

## 1. Security Position

عبارة “maximum security” ليست requirement. المطلوب controls قابلة للاختبار وowners واضحون.

الأصول الأعلى حساسية:

- SQL write credentials.
- Connector private keys.
- Gemini credentials.
- invoice images وOCR content.
- admin identities.
- commit/audit journal.

## 2. Identity Model

### Android Device

- one-time QR bootstrap.
- device key generated locally ومحمية بـ Android Keystore.
- revocable device registration.
- short-lived access tokens بعد mutual proof.
- no shared permanent pharmacy token.
- device pairing يثبت الجهاز فقط؛لا يمنح أي pharmacy-human role أوactor identity.

### Local Pharmacy Human

The normative local identity،session،role-provisioning،step-up and actor-audit boundary is [ADR-012](decisions/ADR-012-local-human-identity-and-step-up.md). The decision is accepted،but its implementation and verification gate remains open؛New Item and every Genius write capability remain unavailable.

- Local Connector هو authority لهويات Operator،Catalog Manager وSupervisor داخل pharmacy workflow؛SaaS Admin identity لا تصبح local pharmacy identity.
- كل شخص له `actorId` ثابت وcredential مستقلة؛shared user accounts وcaller-supplied actor names ممنوعة.
- paired device + authenticated human session هما target المطلوب لكل review/business command. Phase 1 runtime لا يحقق human-session boundary بعد،ولذلك device pairing وحدها لا تغلق acceptance. session مربوطة بالـ actor والجهاز وتخضع لـ idle/absolute expiry وrole-version revocation.
- privilege change،account recovery وSupervisor approval تنتج security events. High-impact action تستخدم fresh step-up مربوطة بالـ exact immutable revision/command and impact hash.
- device revocation،user disable أوrole-version change تبطل sessions/approvals ذات الصلة فورًا.

### Local Connector

- unique certificate لكل installation.
- private key non-exportable عندما يدعم Windows certificate store ذلك.
- rotation مستقلة عن subscription duration.
- SaaS revocation immediately blocks new cloud jobs.

### Admin

- Supabase Auth.
- mandatory TOTP MFA و`aal2` enforcement server-side للـ privileged routes.
- RBAC منفصل: Billing،Support،Security،Tenant Operations.
- session lifetime وre-authentication للعمليات الحساسة.

## 3. Network Model

- لا inbound Internet إلى Connector.
- Connector يبدأ outbound HTTPS/mTLS فقط.
- Android Local API على pharmacy LAN مع TLS ولا يعتمد على network trust.
- Genius DB غير مكشوف للـ Android أوSaaS.
- SQL network access محصور في Connector host/firewall policy.
- production SQL 2008 R2 يحتاج build يدعم TLS 1.2؛ إن لم يمكن تحديثه، الاتصال يبقى local/isolated ويُسجل risk acceptance صريح.

## 4. Secrets

- Gemini credentials مركزية في cloud KMS/Secret Manager.
- لا token لكل صيدلية.
- لا secrets داخل Android APK أوrepository أوlogs.
- SQL credentials تحفظ محليًا باستخدام Windows DPAPI/machine certificate.
- environment variables ليست source دائم للأسرار في Production.
- rotation runbooks واختبار revocation إلزاميان.

## 5. Authorization

- deny by default.
- tenant identity مشتقة من authenticated principal، لا من request body وحده.
- actor identity والأدوار مشتقة من Connector-authenticated human principal،لا من Android payload أوdevice display name.
- New Item permission مستقلة.
- Commit permission مستقلة ويمكن جعلها Supervisor-only.
- expiry override وduplicate override تحتاج step-up/local supervisor approval.
- approval token single-use ومربوطة بالـ action،immutable revision/command،initiator،approver،device،impact hash وshort expiry؛أي revision جديدة تلغي approval السابقة.
- DB account يحصل على object-level grants اللازمة للـ certified profile فقط.

### Offline Genius Write Authorization

The normative capability،TTL،trusted-time،revocation and emergency semantics are [ADR-013](decisions/ADR-013-offline-genius-write-entitlements.md).

- OCR entitlement لا تمنح Genius write authority. `GENIUS_MASTER_ITEM_CREATE` و`GENIUS_PURCHASE_COMMIT` capabilities منفصلة وموقعة ومربوطة بالـ Connector،profile والـ approved fingerprint.
- production default TTL ثماني ساعات والحد الأقصى المطلق 24 ساعة؛offline state لا يمدد grant.
- Connector يتحقق من grant عند queueing وقبل فتح DB transaction. confirmation قبل expiry لا تحجز حق Commit بعد expiry.
- trusted time أثناء نفس service run يُشتق من آخر signed server time + monotonic elapsed time،لا من wall clock وحده. service/OS restart،protected-state loss أوأي ambiguity توقف writes حتى online revalidation؛generation وcapability-scoped issuance sequence تمنع regression أوsubstitution.
- لا offline break-glass أوsupport bypass. Manual e-plus entry هو emergency write path،بينما capture/stored review وread-only reconciliation تبقى متاحة.

## 6. Data Protection

### In Transit

- TLS 1.2 minimum؛ TLS 1.3 حيث يدعم الطرفان.
- mTLS بين Connector وSaaS.
- certificate pinning policy على Android تُدار بحذر مع rotation؛ الاعتماد الأساسي على platform trust + device identity.

### At Rest

- temporary documents encrypted بمفتاح per-object أوper-job.
- Sidecar encrypted storage أوvolume encryption مع application-level protection للـ secrets.
- SaaS PostgreSQL وobject storage encryption.
- backups encrypted ومفاتيحها منفصلة.

### Retention

- raw invoice default TTL قصير ومعلن.
- deletion job له verification وmetrics.
- audit يحتفظ بالـ hashes والmetadata بعد حذف الصورة.
- raw content لا يدخل application logs أوanalytics.

## 7. File Security

- magic-byte validation.
- Android يقبل PDF user input فقط،يفحصه ويرسم كل صفحة إلى ordered normalized JPEG قبل الرفع. Local API وConnector ↔ SaaS لا يقبلان raw PDF؛limits وquota تعد كل normalized upload/page-array index مرة واحدة.
- PDF parser limits/fuzzing وplatform isolation حيث تدعمها Android؛أي parser مستقبلي خارج هذا المسار يحتاج process isolation مستقلة.
- malware scanning.
- page/file/decompression limits.
- reject active content وembedded attachments.
- randomized object keys؛ لا تستخدم user filename كمسار.
- content hash للdeduplication والتحقيق.

## 8. Admin Break-Glass

الوصول إلى raw tenant data غير متاح افتراضيًا. Break-glass يحتاج:

1. support/security case.
2. explicit reason.
3. `aal2` step-up.
4. time-bound scope.
5. immutable audit.
6. tenant notification وفق incident policy.
7. automatic expiry.

“Admin can view anything” محذوفة نهائيًا من التصميم.

## 9. Audit Events

- device paired/revoked.
- Connector activated/certificate rotated.
- OCR reservation/settlement.
- every user correction.
- mapping confirmation/rejection.
- local human enrolled/disabled،credential recovered،role changed،session revoked وstep-up approved/rejected.
- New Item creation.
- expiry/duplicate override.
- commit start/result.
- reconciliation checks.
- admin privilege/break-glass activity.

Audit event يحتوي actor،tenant،action،target reference،timestamp،result وcorrelation ID دون raw invoice content.

## 10. Threats That Remain

- unsupported legacy SQL Server خارج الدعم.
- malicious أوcompromised pharmacy workstation.
- incorrect reverse-engineered financial rule.
- e-plus concurrent writer لا يحترم Connector application lock.
- insider with direct SQL access.
- OCR data exposure لدى external provider وفق commercial/privacy terms.

هذه المخاطر لا تختفي باستخدام كلمة Zero Trust؛ تحتاج compensating controls،testing وformal acceptance.

## 11. Security Acceptance

- لا secret scan findings عالية الخطورة.
- Android release لا يحتوي Gemini/SQL secrets.
- mandatory MFA enforced by backend،لا UI فقط.
- connector revocation test ناجح.
- local user provisioning،session expiry/revocation،role-version invalidation،shared-credential prohibition وstep-up binding tests ناجحة.
- offline write grant separation،expiry،replay،clock rollback،generation regression،revocation وno-bypass tests ناجحة.
- tenant isolation tests ناجحة.
- file parser fuzz/limit tests ناجحة.
- SQL least-privilege test يثبت أن account لا يستطيع schema changes أوunrelated reads.
- restore/audit integrity test ناجح.
