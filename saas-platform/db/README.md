# SaaS PostgreSQL migrations

Migration files are append-only after deployment. A new database is created by
running `apply-all.psql`, which applies `001-phase-1.sql` and then
`002-phase-1-hardening.sql` with `ON_ERROR_STOP` enabled:

```powershell
psql $env:PHARMA_AUTO_SAAS_POSTGRES -f saas-platform/db/apply-all.psql
```

An existing database that already ran the pre-hardening `001-phase-1.sql` must
run only `002-phase-1-hardening.sql`. Migration 002 is transaction-wrapped and
safe to re-run. It validates tenant bindings, reconciles recoverable legacy OCR
reservation states, rebuilds quota counters from the reservation ledger,
backfills attempt IDs, and fails closed when data cannot be repaired safely.
Run migrations with the schema owner or a dedicated migration role that can
bypass forced row-level security; the runtime application role must not receive
that privilege.
