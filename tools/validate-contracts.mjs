import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(toolDirectory, "..");
const schemaDirectory = path.join(repositoryRoot, "contracts", "schemas");
const exampleDirectory = path.join(repositoryRoot, "contracts", "examples");
const openApiDirectory = path.join(repositoryRoot, "contracts", "openapi");

const schemaFiles = [
  "commercial-values.v1.schema.json",
  "posting-line.v1.schema.json",
  "invoice-revision.v1.schema.json"
];

const schemas = await Promise.all(
  schemaFiles.map(async (fileName) =>
    JSON.parse(await readFile(path.join(schemaDirectory, fileName), "utf8"))
  )
);

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);
for (const schema of schemas) {
  ajv.addSchema(schema);
}

const invoiceSchemaId =
  "https://schemas.pharma-auto.invalid/v1/invoice-revision.schema.json";
const validateInvoice = ajv.getSchema(invoiceSchemaId);
if (!validateInvoice) {
  throw new Error(`Schema was not registered: ${invoiceSchemaId}`);
}

const invoiceExample = JSON.parse(
  await readFile(
    path.join(exampleDirectory, "invoice-revision.v1.example.json"),
    "utf8"
  )
);

if (!validateInvoice(invoiceExample)) {
  throw new Error(
    `Invoice example does not match v1 schema:\n${JSON.stringify(
      validateInvoice.errors,
      null,
      2
    )}`
  );
}

const invalidDiscountExample = structuredClone(invoiceExample);
invalidDiscountExample.postingLines[0].commercialValues.discounts[1].kind =
  "AMOUNT";
if (validateInvoice(invalidDiscountExample)) {
  throw new Error("The v1 schema accepted a non-percentage second discount.");
}

const openApi = JSON.parse(
  await readFile(path.join(openApiDirectory, "local-connector.v1.json"), "utf8")
);
if (openApi.openapi !== "3.1.0") {
  throw new Error("Local Connector contract must use OpenAPI 3.1.0.");
}

const requiredPaths = [
  "/health/live",
  "/api/v1/invoice-revisions/{revisionId}/posting-lines/{postingLineId}/commercial-edit-preview"
];
for (const requiredPath of requiredPaths) {
  if (!openApi.paths?.[requiredPath]) {
    throw new Error(`OpenAPI path is missing: ${requiredPath}`);
  }
}

console.log(
  `Validated ${schemaFiles.length} schemas, 1 invoice example, 1 negative contract case, and ${requiredPaths.length} OpenAPI paths.`
);
