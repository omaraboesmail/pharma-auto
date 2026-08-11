# ADR-006: Generated Identity as Vendor Number Fallback

**Status:** Accepted

## Context

Vendor invoice number قد يكون مفقودًا أومعاد الاستخدام. `pth_id` هو identity داخلي،وعدد rows لا يساوي أعلى identity بسبب gaps.

## Decision

نحتفظ بـ source number عندما يكون unique داخل Vendor. Actual duplicate تُمنع. Missing/reused-but-distinct invoice تستخدم generated `pth_id` string في `ven_bill_no` مع حفظ source value في Sidecar audit.

## Consequences

- ممنوع `COUNT + 1` و`MAX + 1`.
- يلزم final duplicate check داخل transaction.
- reports تستطيع تمييز source/effective/internal numbers.
