import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const clientPath = new URL("../src/api/client.ts", import.meta.url);
const schemaPath = new URL("../src/api/schema.d.ts", import.meta.url);

test("frontend uses the typed system version contract", async () => {
  const [client, schema] = await Promise.all([
    readFile(clientPath, "utf8"),
    readFile(schemaPath, "utf8"),
  ]);

  assert.match(client, /paths\["\/api\/system\/version"\]/);
  assert.match(client, /\/api\/system\/version/);
  assert.match(schema, /SystemVersionResponse/);
});
