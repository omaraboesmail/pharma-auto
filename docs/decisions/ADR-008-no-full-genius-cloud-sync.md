# ADR-008: No Full Genius Cloud Sync

**Status:** Accepted

## Context

رفع catalog/stock/transactions كاملة إلى SaaS يزيد privacy،staleness وtenant-isolation risk دون حاجة للـ OCR.

## Decision

Genius catalog يبقى local projection. SaaS يخزن Canonical Pharma Catalog وanonymized mapping evidence فقط،ولا يخزن current stock أوfull local transaction history.

## Consequences

- local search/index إلزامي.
- SaaS لا يستطيع اختيار `itm_id` مباشرة.
- pgvector يعمل على canonical/mapping data،لا نسخة خام من Genius.
