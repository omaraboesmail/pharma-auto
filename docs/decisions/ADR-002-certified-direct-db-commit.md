# ADR-002: Certified Direct DB Commit

**Status:** Accepted with production gate

## Context

لا يوجد Beconnect API/contract متوقع. UI Automation هش،لكن direct insertion قد يسبب silent stock/financial corruption لأن Purchase business logic غير موجودة في Stored Procedure واحدة.

## Decision

Direct DB Commit هو production target عبر versioned profile `EPLUS_GENIUS_DB539_PROFILE_1`. لا يتفعل live إلا بعد Golden e-plus scenarios،fault injection وmandatory reconciliation.

## Why

- أسرع وأوضح تشغيليًا من UI Automation بعد certification.
- schema مفترض ثباتها ويمكن fingerprinting لها.
- يسمح بـ deterministic idempotency وpostconditions.

## Consequences

- integration unsupported وتتحمل المنصة كامل risk.
- أي fingerprint drift يوقف writes.
- accounting/stock scenarios تحتاج evidence وليس schema inference.
- manual e-plus entry يظل recovery path.
