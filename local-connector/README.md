# Local Connector

The Local Connector is the Windows-side authority for local identity, catalog projection, matching, durable jobs, certified Genius writes and reconciliation.

## Phase 1 Projects

- `src/Connector.Domain`: job, pairing, catalog, revision and commercial models.
- `src/Connector.Application`: pairing/authentication, catalog projection/search, durable invoice workflow and immutable review guard.
- `src/Connector.Infrastructure`: DPAPI keys/secrets, AES-GCM document storage, SQLite Sidecar, Microsoft Defender inspection, SELECT-only Genius reader and signed/mTLS SaaS client.
- `src/Connector.LocalApi`: HTTPS Android/control API and Windows Service host.
- `src/Connector.ControlUi`: elevated WPF health, pairing, catalog, queue and revocation UI.
- `installer/`: visible publish/install/uninstall workflow with signed manifests and fail-closed production configuration.

The API supports capture through confirmed review only. It returns `commitAvailable: false` and `geniusWritePerformed: false`; no Genius adapter write project or commit endpoint exists.

## Build and Verification

The repository pins .NET SDK `10.0.302` in `global.json` and commits package lock files.

```powershell
dotnet restore src/Connector.LocalApi/PharmaAuto.Connector.LocalApi.csproj --locked-mode
dotnet build src/Connector.LocalApi/PharmaAuto.Connector.LocalApi.csproj --no-restore
dotnet build src/Connector.ControlUi/PharmaAuto.Connector.ControlUi.csproj --no-restore
```

## Certified-Write Gate

No commercial write may be added until Golden scenarios and the DB fingerprint prove:

- stock-class isolation for new-price receipts.
- preservation of old class quantities and prices.
- both sequential discount translations and rounding.
- tax-inclusive selling-price storage and read-back.
- rollback, connection-loss and reconciliation behavior.

Catalog projection preserves source-byte hashes, unmodified reversed/code-page decoded strings and quality flags. Byte reversal is not canonicalization; manual labels remain Sidecar overlays rather than silent Genius rewrites.

## Installer

Create a self-contained unsigned lab package with `installer/Publish-Phase1Connector.ps1`. Production-style installation rejects unsigned binaries and requires Connector TLS plus SaaS mTLS certificates. `Install-PharmaAutoConnector.ps1` is deliberately visible, elevated and confirmation-gated; it stores the SaaS HMAC secret and SELECT-only Genius connection with machine DPAPI, grants only the virtual service account access, and opens only the configured port to the Private/Domain local subnet.
