# Repository Guidelines

## Project Structure & Module Organization

Pharma Auto is a documentation-first monorepo. Product and architecture requirements live in `PRODUCT.md` and `docs/`; record consequential design changes in `docs/decisions/`. Runtime boundaries are:

- `android-client/`: Kotlin capture and invoice-review app.
- `local-connector/`: .NET Windows service, local API, Genius adapter, and reconciliation.
- `saas-platform/`: .NET API, domain, persistence, and OCR workers.
- `admin-portal/`: Next.js administration UI.
- `contracts/`: versioned OpenAPI, JSON Schema, events, and synthetic examples.
- `infra/`: environments, reusable modules, monitoring, and policies.

Keep domain code independent of UI, HTTP, databases, and Gemini.

## Build, Test, and Development Commands

No Gradle, .NET, or Node build manifests are committed yet, so there is no repository-wide build command. Use these checks now:

```powershell
git diff --check
git status --short
Invoke-Item docs/artifacts/pharma-invoice-bridge.html
```

They check whitespace, reveal unintended files, and preview the architecture artifact. When adding an executable module, commit its lockfile and document exact build, lint, and test commands in its `README.md` and CI.

## Coding Style & Naming Conventions

Use UTF-8, four-space indentation for Kotlin/C#, and two spaces for TypeScript, JSON, and YAML. Use `PascalCase` for types and React components, `camelCase` for functions/variables, and kebab-case documentation filenames. Preserve raw Arabic/English values byte-for-byte; isolate mixed-direction text when rendering and never insert BiDi marks into persisted or matching data.

## Testing Guidelines

Place tests under each module's `tests/` tree. Name Kotlin tests `*Test.kt`, .NET tests `*Tests.cs`, Vitest tests `*.test.ts(x)`, and Playwright scenarios `*.spec.ts`. Android uses JUnit/Compose UI/Espresso; Admin uses Vitest, Testing Library, and Playwright. Genius write changes require Golden DB scenarios, reconciliation assertions, rollback/fault tests, and a certified DB fingerprint. The acceptance gate is 100% reconciliation coverage, not a generic unit-test percentage. Use synthetic, redacted fixtures only.

## Commit & Pull Request Guidelines

Recent history uses short, imperative, sentence-case subjects (for example, `Enhance text integrity and BiDi handling`). Conventional Commits are optional. Target feature PRs to `dev`; PRs to `main` must originate from `dev`. Include scope, rationale, test evidence, linked issue/ADR where applicable, and screenshots for UI changes. Request the owners listed in `docs/08-repository-structure.md`; Genius adapter changes require a second reviewer.

## Security & Configuration

Never commit `Genius.bak`, `Order-Automating/`, production invoices, credentials, certificates, `.env*`, or runtime diagnostics. Do not place Gemini tokens or SQL credentials in Android or browser code. Every Genius commit must use an approved profile and end with reconciliation.
