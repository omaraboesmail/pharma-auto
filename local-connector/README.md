# Local Connector

The Local Connector is the Windows-side authority for local identity, catalog projection, matching, durable jobs, certified Genius writes and reconciliation.

## Initialized Projects

- `src/Connector.Domain`: ERP-neutral commercial rules.
- `src/Connector.Application`: contract mapping and commercial-edit preview use case.
- `src/Connector.LocalApi`: Android LAN API and OpenAPI endpoint.
- `tests/Unit/Connector.Domain.Tests`: commercial formula and safety-policy tests.
- `tests/Integration/Connector.LocalApi.Tests`: liveness and preview endpoint tests.

The initialized endpoint validates and previews edits only. It returns `geniusWritePerformed: false`; no Genius adapter write project exists yet.

## Build and Test

The repository pins .NET SDK `10.0.302` in `global.json` and commits package lock files.

```powershell
dotnet restore PharmaAuto.Connector.slnx --locked-mode
dotnet build PharmaAuto.Connector.slnx --no-restore
dotnet format PharmaAuto.Connector.slnx --verify-no-changes --no-restore
dotnet test PharmaAuto.Connector.slnx --no-build --no-restore
dotnet list PharmaAuto.Connector.slnx package --vulnerable --include-transitive
```

## Certified-Write Gate

No commercial write may be added until Golden scenarios and the DB fingerprint prove:

- stock-class isolation for new-price receipts.
- preservation of old class quantities and prices.
- both sequential discount translations and rounding.
- tax-inclusive selling-price storage and read-back.
- rollback, connection-loss and reconciliation behavior.

Catalog projection preserves raw name bytes, hashes and quality flags. Byte reversal is not canonicalization; manual labels remain Sidecar overlays rather than silent Genius rewrites.
