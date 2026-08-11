# ADR-007: Central OCR Credentials

**Status:** Accepted

## Context

Gemini token لكل صيدلية مربوط بمدة subscription يزيد secrets ويخلط provider credentials مع billing model.

## Decision

Gemini credentials مركزية في SaaS KMS وتدور وفق security policy. Tenant usage يدار بالـ quota ledger ولا يرى Android/Connector token.

## Consequences

- SaaS مسؤول عن provider cost protection.
- credential incident يؤثر blast radius أكبر ويحتاج pool/rotation controls.
- لا secrets داخل APK.
