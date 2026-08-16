# ADR-010: Raw Names and BiDi Are Separate Concerns

**Status:** Accepted

## Context

`REVERSE` يفك byte reversal في حقول `itm_name_*_encrypt`،لكنه لا يصلح corruption داخل النص. في `itm_id = 60495` الحقلان Arabic/English متطابقان،والحرف المطلوب غير موجود في bytes أصلًا. إعادة ترتيب fragments heuristic غير قابلة للتعميم.

## Decision

النتيجة تُسمى raw label وتُوسم quality flags. Product identity تعتمد أولًا على `itm_int_code`،barcode،Vendor code وconfirmed mappings. Canonical/manual label overlay تُحفظ خارج Genius. UI تستخدم content-derived direction وUnicode BiDi isolation لعرض mixed scripts دون تعديل القيمة.

## Consequences

- name-only auto-match ممنوع عند corruption flags.
- RTL marks presentation-only ولا تدخل DB أوnormalization.
- لا auto-correction من أنماط character movement.
- manual review تزيد لبعض Items،لكن false Pharma match أخطر من unresolved label.
- Android/Web يحتاجان regression tests للـ mixed Arabic/English والنصوص التالفة.
