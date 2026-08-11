# Security and Privacy Specification

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
- New Item permission مستقلة.
- Commit permission مستقلة ويمكن جعلها Supervisor-only.
- expiry override وduplicate override تحتاج step-up/local supervisor approval.
- DB account يحصل على object-level grants اللازمة للـ certified profile فقط.

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
- PDF parser sandbox/process isolation.
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
- tenant isolation tests ناجحة.
- file parser fuzz/limit tests ناجحة.
- SQL least-privilege test يثبت أن account لا يستطيع schema changes أوunrelated reads.
- restore/audit integrity test ناجح.
