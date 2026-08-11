# ADR-004: Expiry Splits Are Posting Lines

**Status:** Accepted

## Context

نفس Item قد يأتي بعدة expiry dates أوحتى نفس expiry بأسعار مختلفة. الـ DB الفعلية تحتوي هذا النمط،و`no_of_items` يطابق detail rows.

## Decision

Source Line تحتفظ بموضع الصورة،وتحتوي على child Posting Lines. بعد review يعاد اشتقاق `posting_sequence` 1..N،وتصبح `ptd_id` داخل invoice.

## Consequences

- إضافة expiry تدفع البنود التالية في final DB order دون فقد source identity.
- totals تُحسب على Posting Lines.
- identical splits تنتج warning لا automatic destructive merge.
