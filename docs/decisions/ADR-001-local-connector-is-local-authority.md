# ADR-001: Local Connector Is the Local Authority

**Status:** Accepted

## Context

Genius DB داخل الصيدلية هي source of truth للـ Items،Vendors،stock وpurchase transactions. SaaS لا يملك DB transaction context ولا يجب أن يحصل على SQL credentials.

## Decision

Local Connector ينفذ catalog projection،local ID resolution،New Item commands،Direct DB Commit وreconciliation. SaaS يدير OCR،subscription،quota وCanonical candidates فقط.

## Why

- يمنع cloud outage من امتلاك DB write credential.
- يقلل cross-tenant data exposure.
- يحافظ على `itm_id` و`ven_id` كـ local identities.
- يسمح بتطبيق DB fingerprint وlocks بالقرب من DB.

## Consequences

- Connector يصبح critical software ويحتاج signed updates وdurable Sidecar.
- support أصعب من pure SaaS.
- matching النهائي يجب أن يعمل حتى لو لم يملك SaaS full catalog.
