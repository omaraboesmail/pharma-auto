# SaaS Platform

Modular monolith لإدارة tenants،subscriptions،quotas،Connector identities،OCR orchestration،Canonical Pharma Catalog،pgvector retrieval،usage وaudit.

## Modules

- API/Application/Domain/Persistence.
- OCR Worker and Gemini Gateway.
- Identity and Connector Registry.
- Subscription and Quota Ledger.
- Canonical Catalog and pgvector Search.
- Audit and Security Events.

SaaS لا يملك SQL credentials للصيدلية ولا يقرر `itm_id`/`ven_id`.
