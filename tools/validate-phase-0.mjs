import { execFileSync } from "node:child_process";
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

await import("./validate-contracts.mjs");

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(toolDirectory, "..");

const requiredFiles = [
  "docs/16-db-fingerprint-definition.md",
  "docs/17-golden-scenario-capture-procedure.md",
  "docs/18-write-assumptions-and-side-effect-owners.md",
  "docs/19-threat-model.md",
  "docs/20-phase-0-closure.md",
  "contracts/schemas/ocr-result.v1.schema.json",
  "contracts/schemas/dataset-manifest.v1.schema.json",
  "contracts/schemas/db-fingerprint-definition.v1.schema.json",
  "contracts/schemas/golden-scenario-manifest.v1.schema.json",
  "contracts/examples/golden-scenario-manifest.v1.example.json",
  "local-connector/profiles/EPLUS_GENIUS_DB539_PROFILE_1/fingerprint-definition.v1.json",
  "test-data/phase-0/manifest.v1.json",
  "test-data/phase-0/generate-fixtures.ps1"
];

const baselineSystemDocuments = [
  "docs/00-vision-and-scope.md",
  "docs/01-system-requirements.md",
  "docs/02-architecture.md",
  "docs/03-workflows-and-domain-model.md",
  "docs/04-genius-db-adapter.md",
  "docs/05-api-and-contracts.md",
  "docs/06-security-and-privacy.md",
  "docs/07-technology-stack.md",
  "docs/08-repository-structure.md",
  "docs/09-testing-and-acceptance.md",
  "docs/10-deployment-and-operations.md",
  "docs/11-delivery-roadmap.md",
  "docs/12-risk-register.md",
  "docs/13-text-integrity-and-bidi.md",
  "docs/14-initialization-decisions.md",
  "docs/15-genius-commercial-evidence.md"
];

async function assertExists(relativePath) {
  try {
    await stat(path.join(repositoryRoot, relativePath));
  } catch {
    throw new Error(`Required Phase 0 artifact is missing: ${relativePath}`);
  }
}

for (const requiredFile of requiredFiles) {
  await assertExists(requiredFile);
}
for (const systemDocument of baselineSystemDocuments) {
  await assertExists(systemDocument);
  const content = await readFile(path.join(repositoryRoot, systemDocument), "utf8");
  if (content.trim().length === 0) {
    throw new Error(`Approved baseline system document is empty: ${systemDocument}`);
  }
}

const roadmap = await readFile(
  path.join(repositoryRoot, "docs", "11-delivery-roadmap.md"),
  "utf8"
);
const requiredRoadmapTerms = [
  "approved system docs",
  "sanitized test dataset",
  "DB fingerprint definition",
  "Golden Scenario capture procedure",
  "versioned domain/OCR contracts",
  "threat model",
  "20-phase-0-closure.md"
];
for (const term of requiredRoadmapTerms) {
  if (!roadmap.toLocaleLowerCase("en-US").includes(term.toLocaleLowerCase("en-US"))) {
    throw new Error(`Phase 0 roadmap term or closure link is missing: ${term}`);
  }
}

const initializationGate = await readFile(
  path.join(repositoryRoot, "docs", "14-initialization-decisions.md"),
  "utf8"
);
if (!/Status:\s*\*\*Initialization approved\*\*/u.test(initializationGate)) {
  throw new Error("The system initialization baseline is not approved.");
}

const decisionsDirectory = path.join(repositoryRoot, "docs", "decisions");
const adrFiles = (await readdir(decisionsDirectory))
  .filter((fileName) => /^ADR-\d+.*\.md$/u.test(fileName))
  .sort();
if (adrFiles.length === 0) {
  throw new Error("No ADRs were found.");
}
for (const adrFile of adrFiles) {
  const content = await readFile(path.join(decisionsDirectory, adrFile), "utf8");
  if (!/^\*\*Status:\*\* Accepted(?: with [^\r\n]+)?$/mu.test(content)) {
    throw new Error(`ADR does not have an accepted status: ${adrFile}`);
  }
}

const assumptionRegister = await readFile(
  path.join(
    repositoryRoot,
    "docs",
    "18-write-assumptions-and-side-effect-owners.md"
  ),
  "utf8"
);
for (let index = 1; index <= 17; index += 1) {
  const id = `WA-${String(index).padStart(3, "0")}`;
  if (!assumptionRegister.includes(id)) {
    throw new Error(`Write assumption is missing from the register: ${id}`);
  }
}

const ownedSurfaces = [
  "dbo.pur_trans_h",
  "dbo.pur_trans_d",
  "dbo.Item_Class",
  "dbo.Item_Class_Store",
  "dbo.Item_Catalog",
  "dbo.Item_Vendor",
  "dbo.Vendor",
  "dbo.F_Auto_Doc_h",
  "dbo.F_Auto_Doc_d",
  "dbo.F_Transaction_Header",
  "dbo.F_Transaction_Bills",
  "dbo.F_Transaction_Header_SaveDeleteRecords",
  "dbo.ICS_Month_Close",
  "dbo.Store",
  "dbo.vendor_credit_chng",
  "dbo.watch_qty_chng",
  "dbo.v_VenTrans",
  "dbo.Sys_setting"
];
for (const surface of ownedSurfaces) {
  if (!assumptionRegister.includes(surface)) {
    throw new Error(`Critical side-effect surface has no recorded owner: ${surface}`);
  }
}
if (!assumptionRegister.includes("Any changed or referenced unlisted object")) {
  throw new Error("The fail-closed owner rule for a newly discovered object is missing.");
}

const threatModel = await readFile(
  path.join(repositoryRoot, "docs", "19-threat-model.md"),
  "utf8"
);
for (let index = 1; index <= 18; index += 1) {
  const id = `TM-${String(index).padStart(2, "0")}`;
  if (!threatModel.includes(id)) {
    throw new Error(`Threat Model entry is missing: ${id}`);
  }
}

const sourceDirectory = path.join(repositoryRoot, "test-data", "phase-0", "sources");
const sourceFiles = (await readdir(sourceDirectory)).sort();
if (
  sourceFiles.length !== 4 ||
  sourceFiles.some((fileName) => !fileName.endsWith(".png"))
) {
  throw new Error("Phase 0 source dataset must contain exactly four generated PNG pages.");
}
const expectedDirectory = path.join(
  repositoryRoot,
  "test-data",
  "phase-0",
  "expected"
);
const expectedFiles = (await readdir(expectedDirectory)).filter((fileName) =>
  fileName.endsWith(".json")
);
if (expectedFiles.length !== 3) {
  throw new Error("Phase 0 dataset must contain exactly three expected OCR results.");
}

const trackedFiles = execFileSync("git", ["ls-files"], {
  cwd: repositoryRoot,
  encoding: "utf8"
})
  .split(/\r?\n/u)
  .filter(Boolean)
  .map((fileName) => fileName.replaceAll("\\", "/"));

const forbiddenTrackedPatterns = [
  /^Genius\.bak$/iu,
  /(?:^|\/)Order-Automating\//iu,
  /(?:^|\/)invoice_examp\//iu,
  /(?:^|\/)WhatsApp Image /iu,
  /\.(?:bak|mdf|ldf|pfx|pem|key)$/iu,
  /(?:^|\/)\.env(?:\.|$)/iu
];
for (const trackedFile of trackedFiles) {
  if (forbiddenTrackedPatterns.some((pattern) => pattern.test(trackedFile))) {
    throw new Error(`Forbidden sensitive/runtime file is tracked: ${trackedFile}`);
  }
}

async function collectMarkdown(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await collectMarkdown(entryPath)));
    } else if (entry.name.endsWith(".md")) {
      files.push(entryPath);
    }
  }
  return files;
}

const markdownFiles = [
  path.join(repositoryRoot, "README.md"),
  ...(await collectMarkdown(path.join(repositoryRoot, "docs"))),
  ...(await collectMarkdown(path.join(repositoryRoot, "contracts"))),
  path.join(repositoryRoot, "test-data", "phase-0", "README.md")
];

const brokenLinks = [];
for (const markdownFile of markdownFiles) {
  const markdown = await readFile(markdownFile, "utf8");
  const linkPattern = /\[[^\]]*\]\(([^)]+)\)/gu;
  for (const match of markdown.matchAll(linkPattern)) {
    const rawTarget = match[1].trim().replace(/^<|>$/gu, "");
    if (
      rawTarget.startsWith("#") ||
      /^(?:https?:|mailto:)/iu.test(rawTarget)
    ) {
      continue;
    }
    const fileTarget = decodeURIComponent(rawTarget.split("#", 1)[0]);
    if (fileTarget.length === 0) {
      continue;
    }
    const resolvedTarget = path.resolve(path.dirname(markdownFile), fileTarget);
    try {
      await stat(resolvedTarget);
    } catch {
      brokenLinks.push(
        `${path.relative(repositoryRoot, markdownFile)} -> ${rawTarget}`
      );
    }
  }
}
if (brokenLinks.length > 0) {
  throw new Error(`Broken local Markdown links:\n${brokenLinks.join("\n")}`);
}

console.log(
  `Phase 0 audit passed: ${baselineSystemDocuments.length} approved baseline system documents, ${requiredFiles.length} required Phase 0 artifacts, ${adrFiles.length} accepted ADRs, 17 registered write assumptions, 18 owned critical surfaces plus unknown-object fallback, 18 threats, 4 synthetic pages, 3 OCR results, no forbidden tracked files, and ${markdownFiles.length} Markdown files with valid local links.`
);
