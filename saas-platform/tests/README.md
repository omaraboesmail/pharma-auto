# SaaS Tests

يشمل unit،API integration،contract،quota concurrency،tenant isolation،pgvector recall وOCR provider failure tests.

PostgreSQL integration tests are opt-in and use isolated schemas in a disposable
PostgreSQL 18 database; they do not start Docker. The connection must belong to
a migration-test role that can install `vector`/`btree_gist`, create temporary
roles, and bypass RLS:

```powershell
$env:PHARMA_AUTO_POSTGRES_TEST_CONNECTION_STRING = 'Host=...;Database=...;Username=...;Password=...'
dotnet test PharmaAuto.Saas.slnx -m:1
```
