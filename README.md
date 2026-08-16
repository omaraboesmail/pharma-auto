# Pharma Invoice Bridge

منصة لإدخال Purchase Invoices إلى e-plus من صور أو PDF، مع OCR، Pharma-aware matching، مراجعة بشرية، Direct DB Commit، ثم mandatory reconciliation للـ stock والـ financial side effects.

هذه المنصة ليست OCR demo وليست script يكتب في `pur_trans_h` و`pur_trans_d`. حدودها الحقيقية هي تحويل مستند غير موثوق إلى transaction قابلة للتدقيق داخل Genius DB قديمة ولا تحتوي على API أو Foreign Keys أو Purchase Stored Procedure رسمية.

## المكونات

| المكون | المسار | المسؤولية |
|---|---|---|
| Android Client | `android-client/` | Capture، review، expiry splitting، product selection، وإنشاء Item بعد موافقة المستخدم |
| Local Connector | `local-connector/` | catalog projection، local matching، durable jobs، Direct DB Commit وreconciliation |
| SaaS Platform | `saas-platform/` | tenants، subscriptions، OCR orchestration، quotas، connector identity وaudit |
| Admin Portal | `admin-portal/` | إدارة الاشتراكات والصحة التشغيلية وbreak-glass access |
| Contracts | `contracts/` | versioned API وevent وOCR schemas |
| Infrastructure | `infra/` | cloud environments، secrets، observability وdeployment policy |
| Documentation | `docs/` | المواصفات والقرارات وخطة الاختبار والتشغيل |

## قواعد غير قابلة للتفاوض

- Gemini لا يختار `itm_id` أو `ven_id` ولا يملك صلاحية Commit.
- Android لا يحتوي على Gemini token أو SQL credentials.
- New Item لا يُنشأ تلقائيًا من OCR؛ يحتاج user confirmation وpermission مستقلة.
- كل expiry/batch يمثل Posting Line مستقلة ويحافظ على ترتيب الفاتورة.
- `pth_id` يُؤخذ من SQL `IDENTITY` ولا يُحسب باستخدام `COUNT + 1` أو `MAX + 1`.
- Duplicate Invoice حقيقية تُمنع؛ لا يتم تغيير رقمها للتحايل على duplicate detection.
- Direct DB Adapter لا يعمل إلا مع DB fingerprint معتمد.
- كل Commit يتبعه reconciliation؛ النجاح الفني للـ SQL transaction وحده لا يكفي.
- `CommitUnknown` لا يعاد تلقائيًا.
- لا يتم تعديل Genius schema أو إضافة جداول خاصة بالمنصة داخله.

## نقطة البداية

1. [Vision and Scope](docs/00-vision-and-scope.md)
2. [System Requirements](docs/01-system-requirements.md)
3. [Architecture](docs/02-architecture.md)
4. [Workflows and Domain Model](docs/03-workflows-and-domain-model.md)
5. [Genius DB Adapter Specification](docs/04-genius-db-adapter.md)
6. [API and Contract Boundaries](docs/05-api-and-contracts.md)
7. [Security and Privacy](docs/06-security-and-privacy.md)
8. [Technology Stack](docs/07-technology-stack.md)
9. [Repository Structure](docs/08-repository-structure.md)
10. [Testing and Acceptance](docs/09-testing-and-acceptance.md)
11. [Deployment and Operations](docs/10-deployment-and-operations.md)
12. [Delivery Roadmap](docs/11-delivery-roadmap.md)
13. [Risk Register](docs/12-risk-register.md)
14. [Text Integrity and BiDi Policy](docs/13-text-integrity-and-bidi.md)
15. [Architecture Decision Records](docs/decisions/README.md)

## System Explainer

[Interactive Architecture, Workflow and Decisions Artifact](docs/artifacts/pharma-invoice-bridge.html) يشرح المكونات،مسار الفاتورة،دور `pgvector` والـ ADRs بصورة تفاعلية responsive.

## حالة الـ repository

هذه Greenfield architecture مستقلة في `C:\Projects\Pharmatech`. مجلد `Order-Automating` خارج حدود المشروع الجديد بالكامل: لا يُستخدم كـ root،ولا Android baseline،ولا مصدر runtime أوdependency. المجلدات الجديدة documentation-first scaffolding وليست implementation.
