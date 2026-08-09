import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const clientPath = new URL("../src/api/client.ts", import.meta.url);
const schemaPath = new URL("../src/api/schema.d.ts", import.meta.url);

test("frontend keeps typed bootstrap and project API contracts", async () => {
  const [client, schema] = await Promise.all([
    readFile(clientPath, "utf8"),
    readFile(schemaPath, "utf8"),
  ]);

  assert.match(client, /paths\["\/api\/system\/version"\]/);
  assert.match(client, /\/api\/system\/version/);
  assert.match(schema, /SystemVersionResponse/);
  assert.match(schema, /"\/api\/projects\/"/);
  assert.match(schema, /ProjectUpsertRequest/);
  assert.match(schema, /ProjectResponse/);
});
