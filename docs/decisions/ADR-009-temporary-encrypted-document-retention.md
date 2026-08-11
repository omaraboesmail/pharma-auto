# ADR-009: Temporary Encrypted Document Retention

**Status:** Accepted

## Context

“No saving, only caching” غير قابل للتدقيق؛ cache هي storage. OCR/retry يحتاجان retention قصيرة ومحددة.

## Decision

الملفات تحفظ temporary بتشفير،object/job keys،TTL وverified deletion. بعد الحذف يبقى hash وoperational audit فقط.

## Consequences

- lifecycle worker وdeletion metrics مطلوبان.
- retry window محدود بالـ TTL.
- raw content ممنوع في logs والـ analytics.
