# Architecture Decision Records

| ADR | القرار | الحالة |
|---|---|---|
| [ADR-001](ADR-001-local-connector-is-local-authority.md) | Local Connector هو authority للـ local ERP identity والـ Commit | Accepted |
| [ADR-002](ADR-002-certified-direct-db-commit.md) | Direct DB Commit عبر certified profile | Accepted with production gate |
| [ADR-003](ADR-003-hybrid-matching-with-pgvector.md) | Hybrid matching وpgvector كـ candidate generator | Accepted |
| [ADR-004](ADR-004-expiry-splits-are-posting-lines.md) | Expiry splits تتحول إلى ordered Posting Lines | Accepted |
| [ADR-005](ADR-005-new-item-is-master-data-command.md) | New Item عملية Master Data مستقلة | Accepted |
| [ADR-006](ADR-006-invoice-number-fallback.md) | `pth_id` الفعلي هو fallback لـ `ven_bill_no` | Accepted |
| [ADR-007](ADR-007-central-ocr-credentials.md) | Gemini credentials مركزية في SaaS | Accepted |
| [ADR-008](ADR-008-no-full-genius-cloud-sync.md) | لا full Genius replication إلى SaaS | Accepted |
| [ADR-009](ADR-009-temporary-encrypted-document-retention.md) | documents تخزن مؤقتًا بتشفير وTTL صريح | Accepted |
| [ADR-010](ADR-010-raw-names-and-bidi.md) | reversed names raw untrusted؛BiDi display بلا heuristic repair | Accepted |
| [ADR-011](ADR-011-commercial-edits-and-stock-class-pricing.md) | Commercial edits remain line-native؛new selling prices require isolated stock classes | Accepted with certified-write gate |

تعديل قرار Accepted يحتاج ADR بديلة تشير إلى القرار السابق،ولا يتم حذف historical rationale.
