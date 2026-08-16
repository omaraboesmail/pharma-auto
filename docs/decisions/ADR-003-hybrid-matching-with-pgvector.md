# ADR-003: Hybrid Matching with pgvector

**Status:** Accepted

## Context

Pharma names noisy ومتعددة اللغات،وقد تكون Genius raw labels نفسها تالفة بعد byte reversal. semantic similarity وحدها قد تخلط strength،form أوpack. يوجد أيضًا احتياج للاستفادة من Canonical Catalog وhistorical cross-pharmacy evidence دون رفع Genius DB كاملة.

## Decision

SaaS PostgreSQL يستخدم `pgvector` مع lexical search وstructured Pharma filters لتوليد Canonical candidates. Exact identifiers وhard constraints تسبق vector retrieval. Corrupted raw-name flags تمنع name-only auto-match. Connector يربط candidate بـ local `itm_id` ويظل المستخدم صاحب القرار النهائي.

## Why

- pgvector يضيف semantic retrieval داخل PostgreSQL الموجود بالفعل.
- لا يحتاج Vector DB مستقلة أوoperations stack جديدة.
- JOINs وmetadata filters وaudit تبقى في نفس transactional store.
- يسمح بتغيير embedding/index version دون تغيير local ERP identity.

## Consequences

- embedding model/version lifecycle مطلوب.
- HNSW يحتاج recall/latency benchmarks وreindex strategy.
- vector distance ليست confidence probability.
- لا auto-confirm بسبب cross-tenant vector result.
- Local Connector يحتفظ بexact/local mapping path عند انقطاع SaaS.
