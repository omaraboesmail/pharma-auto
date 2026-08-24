import { createHash } from "node:crypto";
import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(toolDirectory, "..");
const schemaDirectory = path.join(repositoryRoot, "contracts", "schemas");
const exampleDirectory = path.join(repositoryRoot, "contracts", "examples");
const openApiDirectory = path.join(repositoryRoot, "contracts", "openapi");
const datasetDirectory = path.join(repositoryRoot, "test-data", "phase-0");
const fingerprintPath = path.join(
  repositoryRoot,
  "local-connector",
  "profiles",
  "EPLUS_GENIUS_DB539_PROFILE_1",
  "fingerprint-definition.v1.json"
);

async function readJson(filePath) {
  return JSON.parse(await readFile(filePath, "utf8"));
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function assertValid(validate, value, label) {
  if (!validate(value)) {
    throw new Error(
      `${label} does not match its schema:\n${JSON.stringify(
        validate.errors,
        null,
        2
      )}`
    );
  }
}

function assertRejected(validate, value, label) {
  if (validate(value)) {
    throw new Error(`Negative contract case was accepted: ${label}`);
  }
}

function resolveInside(baseDirectory, relativePath, label) {
  const resolved = path.resolve(baseDirectory, relativePath);
  const relative = path.relative(baseDirectory, resolved);
  if (relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new Error(`${label} escapes its allowed directory: ${relativePath}`);
  }
  return resolved;
}

function pngDimensions(bytes, label) {
  const pngSignature = "89504e470d0a1a0a";
  if (bytes.length < 24 || bytes.subarray(0, 8).toString("hex") !== pngSignature) {
    throw new Error(`${label} is not a valid PNG fixture.`);
  }
  return {
    width: bytes.readUInt32BE(16),
    height: bytes.readUInt32BE(20)
  };
}

function findForbiddenOcrKeys(value, currentPath = "$") {
  const forbiddenKeys = new Set([
    "itm_id",
    "itmId",
    "ven_id",
    "venId",
    "c_id",
    "cId",
    "pth_id",
    "pthId",
    "sql",
    "sqlText"
  ]);
  const findings = [];

  if (Array.isArray(value)) {
    value.forEach((item, index) => {
      findings.push(...findForbiddenOcrKeys(item, `${currentPath}[${index}]`));
    });
  } else if (value && typeof value === "object") {
    for (const [key, item] of Object.entries(value)) {
      if (forbiddenKeys.has(key)) {
        findings.push(`${currentPath}.${key}`);
      }
      findings.push(...findForbiddenOcrKeys(item, `${currentPath}.${key}`));
    }
  }

  return findings;
}

const schemaFiles = (await readdir(schemaDirectory))
  .filter((fileName) => fileName.endsWith(".schema.json"))
  .sort();

const schemas = await Promise.all(
  schemaFiles.map((fileName) => readJson(path.join(schemaDirectory, fileName)))
);

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
for (const schema of schemas) {
  ajv.addSchema(schema);
}

const schemaIds = {
  catalogCandidate:
    "https://schemas.pharma-auto.invalid/v1/catalog-candidate.schema.json",
  entitlement: "https://schemas.pharma-auto.invalid/v1/entitlement.schema.json",
  invoice: "https://schemas.pharma-auto.invalid/v1/invoice-revision.schema.json",
  invoiceJob: "https://schemas.pharma-auto.invalid/v1/invoice-job.schema.json",
  ocr: "https://schemas.pharma-auto.invalid/v1/ocr-result.schema.json",
  pairing: "https://schemas.pharma-auto.invalid/v1/pairing-session.schema.json",
  review: "https://schemas.pharma-auto.invalid/v1/review-package.schema.json",
  dataset: "https://schemas.pharma-auto.invalid/v1/dataset-manifest.schema.json",
  fingerprint:
    "https://schemas.pharma-auto.invalid/v1/db-fingerprint-definition.schema.json",
  golden:
    "https://schemas.pharma-auto.invalid/v1/golden-scenario-manifest.schema.json"
};

const validators = Object.fromEntries(
  Object.entries(schemaIds).map(([name, schemaId]) => {
    const validate = ajv.getSchema(schemaId);
    if (!validate) {
      throw new Error(`Schema was not registered: ${schemaId}`);
    }
    return [name, validate];
  })
);

const invoiceExample = await readJson(
  path.join(exampleDirectory, "invoice-revision.v1.example.json")
);
assertValid(validators.invoice, invoiceExample, "Invoice example");

const phaseOneExamples = [
  [
    validators.pairing,
    "pairing-session.v1.example.json",
    "Pairing session example"
  ],
  [validators.entitlement, "entitlement.v1.example.json", "Entitlement example"],
  [validators.review, "review-package.v1.example.json", "Review package example"],
  [
    validators.catalogCandidate,
    "catalog-candidate.v1.example.json",
    "Catalog candidate example"
  ],
  [validators.invoiceJob, "invoice-job.v1.example.json", "Invoice job example"]
];
for (const [validate, fileName, label] of phaseOneExamples) {
  assertValid(validate, await readJson(path.join(exampleDirectory, fileName)), label);
}

const expiredPairingShape = await readJson(
  path.join(exampleDirectory, "pairing-session.v1.example.json")
);
expiredPairingShape.baseUrl = "http://192.0.2.10:7080";
assertRejected(validators.pairing, expiredPairingShape, "cleartext pairing URL");

const unsafeEntitlement = await readJson(
  path.join(exampleDirectory, "entitlement.v1.example.json")
);
unsafeEntitlement.geniusWritesAllowed = true;
assertRejected(
  validators.entitlement,
  unsafeEntitlement,
  "Phase 1 entitlement that enables Genius writes"
);

const autoConfirmedCandidate = await readJson(
  path.join(exampleDirectory, "catalog-candidate.v1.example.json")
);

const unsafeReviewPackage = await readJson(
  path.join(exampleDirectory, "review-package.v1.example.json")
);
unsafeReviewPackage.geniusWritePerformed = true;
assertRejected(
  validators.review,
  unsafeReviewPackage,
  "review package that reports a Genius write"
);
autoConfirmedCandidate.requiresManualConfirmation = false;
assertRejected(
  validators.catalogCandidate,
  autoConfirmedCandidate,
  "catalog candidate that bypasses manual confirmation"
);

const invalidDiscountExample = structuredClone(invoiceExample);
invalidDiscountExample.postingLines[0].commercialValues.discounts[1].kind =
  "AMOUNT";
assertRejected(
  validators.invoice,
  invalidDiscountExample,
  "non-percentage second discount"
);

let canonicalNumericNegativeCaseCount = 0;
const boundaryInvoiceExample = structuredClone(invoiceExample);
boundaryInvoiceExample.postingLines[0].quantity = "999999999999.999999";
boundaryInvoiceExample.postingLines[0].commercialValues.purchaseUnitPrice =
  "999999999999.999999";
boundaryInvoiceExample.postingLines[0].commercialValues.sellingUnitPrice =
  "999999999999.999999";
boundaryInvoiceExample.postingLines[0].commercialValues.discounts[0].percentage =
  "100.0000";
assertValid(
  validators.invoice,
  boundaryInvoiceExample,
  "Invoice canonical numeric boundaries"
);

const invalidDecimalLexemes = [
  ["-1", "signed decimal"],
  ["1e3", "decimal exponent"],
  ["1,000", "grouped decimal"],
  [".5", "decimal without integer digits"],
  ["1.", "decimal without fractional digits"],
  ["01", "decimal with a leading zero"],
  ["1.1234567", "decimal with excessive scale"],
  ["1000000000000", "decimal exceeding DECIMAL(18,6) integer precision"]
];
for (const [value, label] of invalidDecimalLexemes) {
  const invalid = structuredClone(invoiceExample);
  invalid.postingLines[0].commercialValues.purchaseUnitPrice = value;
  assertRejected(validators.invoice, invalid, label);
  canonicalNumericNegativeCaseCount += 1;
}

for (const [value, label] of [
  ["99.99999", "percentage with excessive scale"],
  ["100.0001", "percentage greater than 100"]
]) {
  const invalid = structuredClone(invoiceExample);
  invalid.postingLines[0].commercialValues.discounts[0].percentage = value;
  assertRejected(validators.invoice, invalid, label);
  canonicalNumericNegativeCaseCount += 1;
}

for (const value of ["1.1234567", "1000000000000"]) {
  const invalid = structuredClone(unsafeReviewPackage);
  invalid.geniusWritePerformed = false;
  invalid.sourceLines[0].postingLines[0].quantity = value;
  assertRejected(
    validators.review,
    invalid,
    `review quantity outside canonical DECIMAL(18,6): ${value}`
  );
  canonicalNumericNegativeCaseCount += 1;
}

const fingerprintDefinition = await readJson(fingerprintPath);
assertValid(
  validators.fingerprint,
  fingerprintDefinition,
  "DB fingerprint definition"
);

const criticalObjectKeys = fingerprintDefinition.criticalObjects.map(
  ({ kind, schema, name }) => `${kind}:${schema}.${name}`
);
if (new Set(criticalObjectKeys).size !== criticalObjectKeys.length) {
  throw new Error("DB fingerprint definition contains duplicate critical objects.");
}

const requiredCriticalObjects = [
  "TABLE:dbo.Item_Catalog",
  "TABLE:dbo.Item_Vendor",
  "TABLE:dbo.Vendor",
  "TABLE:dbo.pur_trans_h",
  "TABLE:dbo.pur_trans_d",
  "TABLE:dbo.Item_Class",
  "TABLE:dbo.Item_Class_Store",
  "TABLE:dbo.F_Auto_Doc_h",
  "TABLE:dbo.F_Auto_Doc_d",
  "TABLE:dbo.F_Transaction_Header",
  "TABLE:dbo.F_Transaction_Bills",
  "TABLE:dbo.F_Transaction_Header_SaveDeleteRecords",
  "TABLE:dbo.ICS_Month_Close",
  "TABLE:dbo.Store",
  "TABLE:dbo.vendor_credit_chng",
  "TABLE:dbo.watch_qty_chng",
  "TABLE:dbo.Sys_setting"
];
for (const objectKey of requiredCriticalObjects) {
  if (!criticalObjectKeys.includes(objectKey)) {
    throw new Error(`DB fingerprint critical object is missing: ${objectKey}`);
  }
}

const requiredInvariantIds = ["DBI-001", "DBI-002", "DBI-003"];
const actualInvariantIds = fingerprintDefinition.namedDataInvariants.map(
  ({ id }) => id
);
for (const invariantId of requiredInvariantIds) {
  if (!actualInvariantIds.includes(invariantId)) {
    throw new Error(`DB fingerprint named invariant is missing: ${invariantId}`);
  }
}

const invalidFingerprint = structuredClone(fingerprintDefinition);
invalidFingerprint.comparisonPolicy.writeCriticalMismatch = "WARN_ONLY";
assertRejected(
  validators.fingerprint,
  invalidFingerprint,
  "fingerprint that allows a write-critical warning"
);

const goldenExample = await readJson(
  path.join(exampleDirectory, "golden-scenario-manifest.v1.example.json")
);
assertValid(validators.golden, goldenExample, "Golden Scenario manifest example");

const invalidGoldenExample = structuredClone(goldenExample);
invalidGoldenExample.capture.allBusinessTablesCompared = false;
assertRejected(
  validators.golden,
  invalidGoldenExample,
  "Golden capture that did not compare every business table"
);

const datasetManifestPath = path.join(datasetDirectory, "manifest.v1.json");
const datasetManifest = await readJson(datasetManifestPath);
assertValid(validators.dataset, datasetManifest, "Phase 0 dataset manifest");

const invalidDatasetManifest = structuredClone(datasetManifest);
invalidDatasetManifest.documents[0].provenance.containsRealData = true;
assertRejected(
  validators.dataset,
  invalidDatasetManifest,
  "dataset fixture marked as containing real data"
);

const documentIds = new Set();
let datasetPageCount = 0;
let ocrExampleCount = 0;

for (const document of datasetManifest.documents) {
  if (documentIds.has(document.documentId)) {
    throw new Error(`Duplicate dataset documentId: ${document.documentId}`);
  }
  documentIds.add(document.documentId);

  const pageHashLines = [];
  for (const [index, page] of document.pages.entries()) {
    if (page.page !== index + 1) {
      throw new Error(
        `${document.documentId} pages must be contiguous and ordered from 1.`
      );
    }

    const pagePath = resolveInside(datasetDirectory, page.path, "Dataset page");
    const pageBytes = await readFile(pagePath);
    const actualHash = sha256(pageBytes);
    if (actualHash !== page.sha256) {
      throw new Error(
        `${document.documentId} page ${page.page} hash mismatch: ${actualHash}`
      );
    }

    const dimensions = pngDimensions(
      pageBytes,
      `${document.documentId} page ${page.page}`
    );
    if (
      dimensions.width !== page.widthPixels ||
      dimensions.height !== page.heightPixels
    ) {
      throw new Error(
        `${document.documentId} page ${page.page} dimensions do not match the manifest.`
      );
    }

    pageHashLines.push(`${page.page}:${actualHash}`);
    datasetPageCount += 1;
  }

  const actualDocumentHash = sha256(
    Buffer.from(`${pageHashLines.join("\n")}\n`, "utf8")
  );
  if (actualDocumentHash !== document.documentSha256) {
    throw new Error(`${document.documentId} logical document hash mismatch.`);
  }

  const expectedPath = resolveInside(
    datasetDirectory,
    document.expectedResult.path,
    "Expected OCR result"
  );
  const expectedResult = await readJson(expectedPath);
  assertValid(
    validators.ocr,
    expectedResult,
    `${document.documentId} expected OCR result`
  );
  if (expectedResult.document.sourceSha256 !== actualDocumentHash) {
    throw new Error(
      `${document.documentId} expected OCR result references the wrong source hash.`
    );
  }
  if (expectedResult.document.pageCount !== document.pages.length) {
    throw new Error(
      `${document.documentId} expected OCR page count does not match the manifest.`
    );
  }

  const forbiddenKeys = findForbiddenOcrKeys(expectedResult);
  if (forbiddenKeys.length > 0) {
    throw new Error(
      `${document.documentId} OCR evidence contains forbidden local/SQL authority fields: ${forbiddenKeys.join(
        ", "
      )}`
    );
  }

  const invalidOcrResult = structuredClone(expectedResult);
  invalidOcrResult.unexpectedProviderInstruction = "write to Genius";
  assertRejected(
    validators.ocr,
    invalidOcrResult,
    `${document.documentId} OCR result with an unexpected field`
  );
  if (ocrExampleCount === 0) {
    const invalidPercentage = structuredClone(expectedResult);
    invalidPercentage.sourceLines[0].discount1Percentage.normalizedValue =
      "99.99999";
    assertRejected(
      validators.ocr,
      invalidPercentage,
      "OCR result with excessive percentage scale"
    );
  }
  ocrExampleCount += 1;
}

const openApi = await readJson(
  path.join(openApiDirectory, "local-connector.v1.json")
);
if (openApi.openapi !== "3.1.0") {
  throw new Error("Local Connector contract must use OpenAPI 3.1.0.");
}

const requiredPaths = [
  "/health/live",
  "/control/v1/health",
  "/control/v1/pairing-sessions",
  "/control/v1/devices",
  "/control/v1/devices/{deviceId}/revoke",
  "/control/v1/catalog/rebuild",
  "/control/v1/jobs",
  "/api/v1/pairing/claim",
  "/api/v1/auth/challenges",
  "/api/v1/auth/tokens",
  "/api/v1/catalog/items/search",
  "/api/v1/catalog/vendors/search",
  "/api/v1/invoice-jobs",
  "/api/v1/invoice-jobs/{jobId}/pages/{page}/chunks/{chunkIndex}",
  "/api/v1/invoice-jobs/{jobId}/upload-status",
  "/api/v1/invoice-jobs/{jobId}/submit",
  "/api/v1/invoice-jobs/{jobId}",
  "/api/v1/invoice-revisions/{revisionId}",
  "/api/v1/invoice-revisions/{revisionId}/edits",
  "/api/v1/invoice-revisions/{revisionId}/confirm",
  "/api/v1/invoice-revisions/{revisionId}/posting-lines/{postingLineId}/commercial-edit-preview"
];
for (const requiredPath of requiredPaths) {
  if (!openApi.paths?.[requiredPath]) {
    throw new Error(`OpenAPI path is missing: ${requiredPath}`);
  }
}

if (Object.keys(openApi.paths).some((apiPath) => /commit/i.test(apiPath))) {
  throw new Error("Phase 1 OpenAPI must not expose a Genius commit path.");
}

const saasOpenApi = await readJson(path.join(openApiDirectory, "saas.v1.json"));
if (saasOpenApi.openapi !== "3.1.0") {
  throw new Error("SaaS contract must use OpenAPI 3.1.0.");
}
const requiredSaasPaths = [
  "/health/live",
  "/api/v1/entitlements/current",
  "/api/v1/signing-keys/current",
  "/api/v1/ocr/jobs/{jobId}/process",
  "/api/v1/ocr/jobs/{jobId}",
  "/api/v1/canonical-products/search"
];
for (const requiredPath of requiredSaasPaths) {
  if (!saasOpenApi.paths?.[requiredPath]) {
    throw new Error(`SaaS OpenAPI path is missing: ${requiredPath}`);
  }
}

const requiredSignedHeaderSchemes = {
  tenantIdHeader: "X-Tenant-Id",
  connectorIdHeader: "X-Connector-Id",
  requestTimestampHeader: "X-Request-Timestamp",
  requestNonceHeader: "X-Request-Nonce",
  contentSha256Header: "X-Content-SHA256",
  requestSignatureHeader: "X-Request-Signature"
};
const productionSecurity = saasOpenApi.security?.find((requirement) =>
  Object.hasOwn(requirement, "mutualTls")
);
if (!productionSecurity) {
  throw new Error("SaaS OpenAPI must require Connector mTLS by default.");
}
for (const [schemeName, headerName] of Object.entries(requiredSignedHeaderSchemes)) {
  const scheme = saasOpenApi.components?.securitySchemes?.[schemeName];
  if (
    !Object.hasOwn(productionSecurity, schemeName) ||
    scheme?.type !== "apiKey" ||
    scheme?.in !== "header" ||
    scheme?.name !== headerName
  ) {
    throw new Error(`SaaS signed-request header is missing or malformed: ${headerName}`);
  }
}

const canonicalSearch =
  saasOpenApi.components?.schemas?.CanonicalSearchRequest?.properties;
if (
  canonicalSearch?.description?.maxLength !== 2000 ||
  canonicalSearch?.limit?.maximum !== 25
) {
  throw new Error(
    "SaaS canonical-search limits must match the runtime (description 2000, limit 25)."
  );
}

const ocrPageMimeTypes =
  saasOpenApi.components?.schemas?.ProcessOcrRequest?.properties?.pages?.items
    ?.properties?.mimeType?.enum;
if (
  JSON.stringify(ocrPageMimeTypes) !== JSON.stringify(["image/jpeg", "image/png"]) ||
  JSON.stringify(saasOpenApi).includes("application/pdf") ||
  JSON.stringify(openApi).includes("application/pdf")
) {
  throw new Error(
    "Connector-to-SaaS transport must accept normalized JPEG/PNG pages only, never raw PDF."
  );
}

const processResponses =
  saasOpenApi.paths?.["/api/v1/ocr/jobs/{jobId}/process"]?.post?.responses ?? {};
for (const status of ["200", "400", "401", "403", "409", "413", "429", "502"]) {
  if (!processResponses[status]) {
    throw new Error(`SaaS OCR process response is undocumented: ${status}`);
  }
}
if (processResponses["402"]) {
  throw new Error("SaaS OCR process must not advertise the obsolete 402 response.");
}

console.log(
  `Validated ${schemaFiles.length} schemas, ${ocrExampleCount} OCR results, ${datasetManifest.documents.length} synthetic documents/${datasetPageCount} PNG pages, ${phaseOneExamples.length + 1} domain examples, 1 Golden manifest, 1 DB fingerprint definition, ${9 + ocrExampleCount + canonicalNumericNegativeCaseCount} negative cases, and ${requiredPaths.length + requiredSaasPaths.length} OpenAPI paths.`
);
