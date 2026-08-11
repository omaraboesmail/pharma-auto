# ADR-005: New Item Is a Master Data Command

**Status:** Accepted

## Context

Invoice قد تحتوي Item غير موجودة محليًا. إنشاؤها داخل invoice write بصورة ضمنية يجعل duplicate detection،units وrecovery غير واضحة.

## Decision

New Item command منفصلة ومسبقة تحتاج `CATALOG_CREATE`،duplicate review،explicit units/conversions ثم read-back verification. نجاحها لا يُلغى تلقائيًا إذا فشلت invoice.

## Consequences

- item قد يبقى `CreatedButNotYetPurchased`،وهو أفضل من direct delete خطر.
- user workflow أطول للـ first purchase.
- audit وtesting منفصلان عن Purchase Commit.
