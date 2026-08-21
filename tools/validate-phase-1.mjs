import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(toolDirectory, "..");

async function text(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), "utf8");
}

async function json(relativePath) {
  return JSON.parse(await text(relativePath));
}

function requireCondition(condition, message) {
  if (!condition) throw new Error(message);
}

const dataset = await json("test-data/phase-0/manifest.v1.json");
const canonicalProducts = await json(
  "saas-platform/src/Saas.Api/data/canonical-products.v1.json"
);
const canonicalIdentifiers = new Set(
  canonicalProducts.flatMap((product) => product.identifiers)
);

let sourceLines = 0;
let checkedFields = 0;
let presentFields = 0;
let exactIdentifierEligible = 0;
let exactIdentifierCovered = 0;
const commercialFields = [
  "description",
  "vendorItemCode",
  "quantity",
  "unit",
  "purchaseUnitPrice",
  "discount1Percentage",
  "discount2Percentage",
  "sellingUnitPrice",
  "expiryDate",
  "batch"
];

for (const document of dataset.documents) {
  const result = await json(path.join("test-data/phase-0", document.expectedResult.path));
  sourceLines += result.sourceLines.length;
  for (const line of result.sourceLines) {
    for (const field of commercialFields) {
      checkedFields += 1;
      if (line[field]?.normalizedValue != null) presentFields += 1;
    }
    const vendorCode = line.vendorItemCode?.normalizedValue;
    if (vendorCode) {
      exactIdentifierEligible += 1;
      if (canonicalIdentifiers.has(vendorCode)) exactIdentifierCovered += 1;
    }
  }
}

requireCondition(
  exactIdentifierEligible > 0 && exactIdentifierCovered === exactIdentifierEligible,
  "Every synthetic source-line identifier must be covered by the canonical seed."
);
requireCondition(
  canonicalProducts.some(
    (product) => product.displayName === "TEST-MED-A 250 mg Capsules" &&
      product.attributes.strength === "250 MG"
  ),
  "The wrong-strength hard-mismatch challenge is missing."
);

const localOpenApi = await json("contracts/openapi/local-connector.v1.json");
const saasOpenApi = await json("contracts/openapi/saas.v1.json");
requireCondition(
  !Object.keys(localOpenApi.paths).some((apiPath) => /commit/i.test(apiPath)),
  "Phase 1 Local API exposes a commit path."
);
requireCondition(
  !Object.keys(saasOpenApi.paths).some((apiPath) => /genius|commit/i.test(apiPath)),
  "Phase 1 SaaS API exposes local ERP authority."
);

const geniusReader = await text(
  "local-connector/src/Connector.Infrastructure/SqlGeniusCatalogReader.cs"
);
requireCondition(
  !/\b(?:INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|CREATE|EXEC(?:UTE)?)\b/i.test(
    geniusReader
  ),
  "Genius catalog reader contains a non-SELECT SQL verb."
);
requireCondition(
  geniusReader.includes("ApplicationIntent = ApplicationIntent.ReadOnly") &&
    geniusReader.includes("READ UNCOMMITTED"),
  "Genius reader lost its explicit read-only behavior."
);

const connectorProgram = await text(
  "local-connector/src/Connector.LocalApi/Program.cs"
);
requireCondition(
  connectorProgram.includes("ClientCertificateThumbprint") &&
    connectorProgram.includes("SaaS mTLS client certificate"),
  "Production Connector mTLS startup gate is missing."
);

const androidBuild = await text("android-client/app/build.gradle.kts");
const androidManifest = await text("android-client/app/src/main/AndroidManifest.xml");
const androidApplication = await text(
  "android-client/app/src/main/java/com/pharmaauto/android/PharmaAutoApplication.kt"
);
requireCondition(androidBuild.includes("minSdk = 28"), "Android minSdk drifted from 28.");
requireCondition(
  androidBuild.includes("libs.androidx.camera") && androidBuild.includes("libs.hilt.android"),
  "Android CameraX or Hilt wiring is missing."
);
requireCondition(
  androidManifest.includes("android.permission.CAMERA") &&
    androidApplication.includes("@HiltAndroidApp"),
  "Android capture or application injection declaration is missing."
);

const schemaFiles = (await readdir(path.join(repositoryRoot, "contracts", "schemas")))
  .filter((name) => name.endsWith(".schema.json"));
requireCondition(
  schemaFiles.includes("review-package.v1.schema.json"),
  "Read-only review package contract is missing."
);

const fieldPresenceRate = presentFields / checkedFields;
console.log(
  JSON.stringify({
    phase: "PHASE_1_READ_ONLY",
    syntheticDocuments: dataset.documents.length,
    syntheticPages: dataset.documents.reduce(
      (total, document) => total + document.pages.length,
      0
    ),
    sourceLines,
    checkedFields,
    presentFields,
    fieldPresenceRate: Number(fieldPresenceRate.toFixed(4)),
    exactIdentifierEligible,
    exactIdentifierCovered,
    contractSchemas: schemaFiles.length,
    geniusWritePaths: 0
  })
);
