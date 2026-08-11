# Local Connector

Windows component المسؤول عن local authority،catalog projection،matching،durable jobs،Genius Direct DB Adapter وreconciliation.

## Projects

- `src/Connector.Service`: Windows background host.
- `src/Connector.ControlUi`: visible pairing/health/queue/recovery UI.
- `src/Connector.LocalApi`: Android LAN API.
- `src/Connector.Application`: job orchestration and use cases.
- `src/Connector.Domain`: ERP-neutral invoice domain.
- `src/Connector.Sidecar`: local durable store.
- `src/Connector.FileSandbox`: validation/normalization/temp retention.
- `src/Connector.Matching`: exact/local/canonical candidate resolution.
- `src/Genius.Profile.Db539`: certified legacy read/write profile.
- `src/Genius.Reconciliation`: independent postconditions.

## Tests

- unit domain tests.
- component integration tests.
- Golden DB tests على restored Clone.
- fault injection وpower-loss tests.

لا implementation قبل تعريف Golden scenarios وDB fingerprint.
