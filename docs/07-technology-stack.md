# Technology Stack

Baseline date: 2026-08-11. كل major version تُثبت في lockfiles ولا تُرفع تلقائيًا في Production.

## 1. Stack Principles

- أقل عدد ممكن من languages: Kotlin للـ Android،C# للـ Connector/SaaS،TypeScript للـ Admin.
- لا microservices قبل وجود scaling boundary حقيقي؛ SaaS يبدأ modular monolith مع workers منفصلة.
- لا ORM فوق Genius write path؛ legacy writes تحتاج explicit SQL وverified transaction semantics.
- managed cloud primitives أفضل من تشغيل Kubernetes/RabbitMQ بلا فريق Operations.
- كل dependency في file parsing أوcrypto تخضع لـ security update policy.

## 2. Android Client

| Layer | الاختيار | السبب |
|---|---|---|
| Language | Kotlin 2.x pinned | native Android ecosystem،coroutines وtype safety |
| UI | Jetpack Compose + Material 3 | مناسب للـ dynamic invoice/expiry editor |
| Architecture | ViewModel + unidirectional data flow + repository boundaries | يمنع business state من الانتشار داخل composables |
| Camera | CameraX | capture lifecycle ودعم أجهزة متنوع |
| Local data | Room | drafts،offline review وsmall cache؛ ليس catalog source of truth |
| Durable work | WorkManager | uploads/retries التي يجب أن تستمر بعد app restart |
| Networking | OkHttp + Retrofit أوKtor Client،اختيار واحد فقط | LAN HTTPS،resumable upload وtimeouts واضحة |
| Dependency injection | Hilt | component lifecycle واختبار replacements |
| Serialization | Kotlinx Serialization | versioned contracts وstrict parsing |
| Tests | JUnit،Compose UI Test،Espresso/MockWebServer | unit،UI وAPI behavior |

Android implementation يبدأ Greenfield بهذه الحدود. Gemini SDK/API key ممنوعان داخل Android؛ Gemini call يوجد في SaaS فقط.

Android official architecture recommendations تضع Compose كـ modern UI toolkit وتوصي بـ WorkManager للأعمال الدائمة: [Android Architecture Recommendations](https://developer.android.com/topic/architecture/recommendations).

## 3. Local Connector

| Layer | الاختيار | السبب |
|---|---|---|
| Runtime | .NET 10 LTS،latest security patch | Windows Service + mature networking/crypto؛ مدعوم حتى 2028 |
| Service host | .NET Worker Service | durable jobs وWindows lifecycle |
| Local API | ASP.NET Core/Kestrel | HTTPS،certificate auth،streaming uploads |
| Control UI | WPF على .NET 10 | Windows-only product وvisible diagnostics؛ لا حاجة cross-platform UI |
| Sidecar | SQLite WAL + application-level encryption للحقول الحساسة + BitLocker requirement | deployment بسيط وsingle-service writer |
| Genius access | raw ADO.NET عبر Microsoft.Data.SqlClient بعد compatibility certification | explicit SQL،transactions وlocking؛ لا EF Core على Genius |
| Sidecar access | EF Core أوlightweight data layer | migrations داخل Sidecar فقط |
| Image processing | OpenCV/OpenCvSharp | deskew،crop،quality metrics |
| PDF validation | qpdf + PDFium-based rendering in isolated process | parsing/normalization وفصل parser risk |
| Malware scan | Microsoft Defender integration على Windows | local file inspection دون خدمة cloud إضافية |
| Observability | OpenTelemetry logs/metrics/traces | correlation من capture إلىreconciliation |
| Packaging | signed MSI/MSIX bootstrapper + Windows Service installer | controlled upgrades،rollback وcertificate setup |

.NET 10 هو Active LTS ومدعوم حتى 14 نوفمبر 2028 طبقًا لـ [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

### Legacy SQL Compatibility Gate

SQL Server 2008 R2 خارج الدعم منذ 2019. Microsoft توضح أن TLS 1.2 يحتاج builds/updates محددة للـ 2008/2008 R2: [TLS 1.2 support for SQL Server](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/connect/tls-1-2-support-microsoft-sql-server).

لذلك:

- Microsoft.Data.SqlClient + .NET 10 يُختبران على build الصيدلية الفعلي.
- إذا تعذر compatibility، يُعزل `LegacyDbBridge` process على .NET Framework 4.8/System.Data.SqlClient بدل تخفيض security للـ Connector كاملًا.
- لا يتم تعطيل TLS system-wide لإجبار الاتصال.
- production prerequisite هو SQL patch أوlocal isolation risk acceptance.

## 4. SaaS Platform

| Layer | الاختيار | السبب |
|---|---|---|
| Runtime/API | ASP.NET Core على .NET 10 LTS | مشاركة contracts/domain patterns مع Connector وتقليل stack diversity |
| Architecture | Modular monolith + separate OCR workers | transaction boundaries واضحة دون microservice overhead |
| Database | PostgreSQL 18.x current minor + pgvector | tenant،quota،audit،outbox،job state وCanonical candidate retrieval |
| Data access | EF Core للـ control DB + explicit SQL للـ quota hot paths | maintainability مع atomic behavior واضح |
| Object storage | managed S3-compatible private storage | temporary encrypted invoice objects وlifecycle deletion |
| OCR | Gemini API Structured Output عبر official Google Gen AI SDK/REST | schema-constrained extraction؛ لا product IDs |
| Background jobs | PostgreSQL job/outbox في البداية؛ managed queue عند scale | يقلل infrastructure قبل إثبات throughput |
| Search | PostgreSQL full-text/trigram + pgvector HNSW بعد baseline exact-search tests | hybrid lexical/semantic retrieval دون Vector DB مستقلة |
| Cache | لا Redis في MVP؛ يضاف فقط لقياس bottleneck مثبت | منع premature distributed state |
| Auth verification | Supabase JWT validation في backend | Admin identity مع backend-enforced MFA |
| Secrets | cloud KMS/Secret Manager | central Gemini/database/certificate secrets |
| Observability | OpenTelemetry + managed metrics/logs/traces | vendor-neutral instrumentation |

PostgreSQL 18 مدعوم حتى نوفمبر 2030 وتجب متابعة current minor releases: [PostgreSQL Versioning Policy](https://www.postgresql.org/support/versioning/).

`pgvector` يدعم exact وapproximate nearest-neighbor search وHNSW/IVFFlat داخل PostgreSQL مع ACID وbackup المعتادين: [pgvector](https://github.com/pgvector/pgvector). الاختيار المبدئي هو exact search أثناء dataset صغير ثم HNSW عندما تثبت benchmarks الحاجة؛ لا يتم اختيار index parameters بالتخمين.

Gemini Structured Output يدعم subset من JSON Schema؛ لذلك schema validation داخل SaaS تظل إلزامية بعد model response: [Gemini Structured Output](https://ai.google.dev/gemini-api/docs/structured-output).

## 5. Admin Portal

| Layer | الاختيار | السبب |
|---|---|---|
| Framework | Next.js 16 Active LTS + React 19 | SSR،secure server boundary وadmin UX ecosystem |
| Language | TypeScript 5.x strict | contract safety |
| Runtime | Node.js 24 LTS | Production LTS؛ لا Current release |
| Auth | Supabase Auth with mandatory TOTP MFA | existing requirement مع `aal2` enforcement |
| UI | accessible component primitives + project design tokens | لا dashboard template مليء بصلاحيات افتراضية |
| Validation | generated OpenAPI client + runtime schema validation | frontend ليس security boundary |
| Tests | Vitest + Testing Library + Playwright | unit،component وcritical E2E |

Node.js توصي باستخدام Active أوMaintenance LTS للإنتاج: [Node.js Releases](https://nodejs.org/en/about/previous-releases). Next.js 16 هو baseline والـ security patches الشهرية تُطبق سريعًا: [Next.js 16](https://nextjs.org/blog/next-16).

Supabase MFA يجب فرضها في backend باستخدام assurance level، لا بمجرد عرض شاشة challenge: [Supabase MFA](https://supabase.com/docs/guides/auth/auth-mfa).

## 6. Contracts and Tooling

- OpenAPI 3.1.
- JSON Schema لكل OCR/domain version.
- generated clients؛ لا duplicate handwritten DTOs بين المكونات.
- UUIDv7 أوtime-sortable IDs للـ SaaS/Sidecar، مع عدم خلطها بـ Genius identities.
- Git + pull requests + required reviews للـ DB Adapter.
- Conventional commits اختيارية؛ ADR إلزامية للقرارات المعمارية المؤثرة.
- secret scanning،dependency scanning،SAST وSBOM في CI.

## 7. Infrastructure

- Docker containers للـ SaaS/Admin فقط.
- managed PostgreSQL وobject storage.
- Terraform أوOpenTofu،اختيار واحد للمشروع.
- GitHub Actions CI/CD.
- separate dev/staging/production accounts/projects.
- signed Connector releases وقناة staged rollout.
- لا Kubernetes في أول deployment.

## 8. Explicit Rejections

- أي legacy server أوUI automation project خارج هذه repository structure ولا يُعاد استخدامه تلقائيًا.
- Gemini SDK داخل Android مرفوض.
- Supabase service role key داخل Admin browser مرفوض.
- EF Core models مباشرة فوق Genius schema مرفوضة للwrite path.
- Vector DB مستقلة في MVP غير ضرورية؛ SaaS PostgreSQL + pgvector وLocal lexical/mapping index يكفيان.
- full Genius replication إلى SaaS مرفوضة.
