# SaaS Platform

Modular monolith لإدارة tenants،subscriptions،quotas،Connector identities،OCR orchestration،Canonical Pharma Catalog،pgvector retrieval،usage وaudit.

## Modules

- ASP.NET Core API،Application،Domain وInfrastructure projects.
- PostgreSQL 18 migration مع forced tenant RLS،atomic quota ledger و`vector(768)` canonical embeddings.
- ES256 signed entitlements وmTLS + HMAC Connector authentication مع nonce replay window.
- strict Gemini Interactions OCR provider وGemini Embedding 2 semantic-query provider؛fixture/Null providers تعمل في Development فقط.
- Identity and Connector Registry.
- Subscription and Quota Ledger.
- Canonical Catalog and pgvector Search.
- Audit and Security Events.

SaaS لا يملك SQL credentials للصيدلية ولا يقرر `itm_id`/`ven_id`.

## Verification

```powershell
dotnet restore src/Saas.Api/PharmaAuto.Saas.Api.csproj --locked-mode
dotnet build src/Saas.Api/PharmaAuto.Saas.Api.csproj --no-restore
```

Production startup refuses in-memory storage،fixture OCR،missing PostgreSQL،missing Gemini secret،missing ES256 key أوdisabled Connector mTLS. No live Gemini call is part of local verification.
