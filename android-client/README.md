# Android Client

الـ Android app هو Capture and Review Client. لا يحتوي على SQL أوGemini credentials ولا ينفذ final product identity decisions دون user confirmation.

## Planned Boundaries

- pairing and Connector identity.
- camera/PDF capture and quality checks.
- invoice review and evidence crops.
- BiDi-safe display للـ Arabic/English mixed labels مع raw-name integrity warnings.
- Vendor/Product local search.
- expiry/batch splitting مع stable Source Line identity.
- New Item wizard للمستخدم المخول.
- commit/reconciliation status.
- offline drafts وdurable uploads.

## Greenfield Boundary

هذا Android Client مشروع جديد مستقل. لا يستورد source،assets،Gradle configuration أوsecrets من `Order-Automating`. يبدأ implementation بعد تثبيت OpenAPI/JSON Schemas واختبارات expiry splitting والـ offline job behavior.

## Dependency Rule

Android يتحدث مع Local Connector فقط. لا اتصال مباشر بـ Genius DB أوGemini. SaaS interaction التي يحتاجها المستخدم تمر عبر Connector.

Product label rendering يستخدم content-derived direction وUnicode isolation. لا يغيّر text order ولا يضيف حروفًا لمحاولة إصلاح raw Genius names.
