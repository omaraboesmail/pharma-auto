# Repository Structure

## 1. Target Monorepo

```text
Pharmatech/
├─ android-client/              # New standalone Android Client
│  ├─ src/
│  │  ├─ app/
│  │  ├─ core/
│  │  └─ features/
│  ├─ tests/
│  └─ README.md
├─ local-connector/
│  ├─ src/
│  │  ├─ Connector.Service/
│  │  ├─ Connector.ControlUi/
│  │  ├─ Connector.LocalApi/
│  │  ├─ Connector.Application/
│  │  ├─ Connector.Domain/
│  │  ├─ Connector.Sidecar/
│  │  ├─ Connector.FileSandbox/
│  │  ├─ Connector.Matching/
│  │  ├─ Genius.Profile.Db539/
│  │  └─ Genius.Reconciliation/
│  ├─ tests/
│  │  ├─ Unit/
│  │  ├─ Integration/
│  │  ├─ GoldenDb/
│  │  └─ FaultInjection/
│  └─ README.md
├─ saas-platform/
│  ├─ src/
│  │  ├─ Api/
│  │  ├─ Application/
│  │  ├─ Domain/
│  │  ├─ Persistence/
│  │  ├─ OcrWorker/
│  │  ├─ GeminiGateway/
│  │  ├─ Identity/
│  │  ├─ Subscription/
│  │  └─ Audit/
│  ├─ tests/
│  │  ├─ Unit/
│  │  ├─ Integration/
│  │  ├─ Contract/
│  │  └─ TenantIsolation/
│  └─ README.md
├─ admin-portal/
│  ├─ src/
│  │  ├─ app/
│  │  ├─ features/
│  │  ├─ components/
│  │  ├─ contracts/
│  │  └─ security/
│  ├─ tests/
│  └─ README.md
├─ contracts/
│  ├─ openapi/
│  ├─ schemas/
│  ├─ events/
│  ├─ examples/
│  └─ README.md
├─ infra/
│  ├─ environments/
│  ├─ modules/
│  ├─ monitoring/
│  ├─ policies/
│  └─ README.md
├─ docs/
│  ├─ decisions/
│  └─ *.md
├─ tools/                       # Developer-only validation utilities later
└─ README.md
```

## 2. Dependency Direction

- `Domain` لا يعتمد على DB،HTTP،Gemini أوUI.
- `Application` يعتمد على Domain وinterfaces.
- infrastructure modules تنفذ interfaces.
- Genius profile لا يُستورد داخل SaaS أوAndroid.
- Reconciliation يعتمد على read contracts،لا على writer internals قدر الإمكان.
- generated contracts تدخل عبر dedicated adapters،لا تنتشر transport DTOs داخل Domain.

## 3. Android Package Direction

عند refactor، ينقسم Android منطقيًا إلى:

- `capture`
- `pairing`
- `invoice-review`
- `catalog-search`
- `catalog-create`
- `commit-status`
- `data-local`
- `data-connector`
- `domain`
- `design-system`

هذه packages تُبنى داخل Android project الجديد ولا تُنقل من legacy projects.

## 4. Genius Profile Isolation

`Genius.Profile.Db539` يحتوي فقط على:

- schema fingerprint.
- catalog readers.
- master-item commands.
- purchase commit commands.
- ID/class resolution.
- financial scenario strategies.
- DB-specific row models.

إضافة ERP جديد مستقبلًا تعني profile جديدة ولا تعدل Android invoice domain إلا إذا كان ERP يفرض capability مختلفة.

## 5. Ownership

| Area | Required reviewer |
|---|---|
| Android UX/domain | Android owner + Product |
| Genius Adapter | DB integration owner + second reviewer |
| Financial formulas | DB integration owner + pharmacy accounting SME |
| Security/auth | Security reviewer |
| Contracts | owners للمكونين المتصلين |
| Infrastructure | Platform/Operations reviewer |
| ADR | Principal architect/owner |

## 6. Files That Must Never Be Committed

- Genius backups وproduction extracts.
- SQL/Gemini/Supabase secrets.
- private certificates/keys.
- raw invoice samples containing real business data.
- `local.properties` أوenvironment secret files.
- generated diagnostic bundles قبل redaction.

`Genius.bak` الحالي يجب أن يبقى خارج repository ويعامل كـ sensitive test asset محلي، وليس source artifact.

مجلد `Order-Automating` ليس جزءًا من الشجرة أعلاه ولا يدخل build،CI،contracts أوdeployment للمشروع الجديد.
