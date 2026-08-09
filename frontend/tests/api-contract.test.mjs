import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const clientPath = new URL("../src/api/client.ts", import.meta.url);
const schemaPath = new URL("../src/api/schema.d.ts", import.meta.url);

test("frontend keeps typed project, analysis, library, provider, and job API contracts", async () => {
  const [client, schema] = await Promise.all([
    readFile(clientPath, "utf8"),
    readFile(schemaPath, "utf8"),
  ]);

  assert.match(client, /paths\["\/api\/system\/version"\]/);
  assert.match(schema, /ProjectUpsertRequest/);
  assert.match(client, /listProjects/);
  assert.match(client, /uploadProjectSong/);
  assert.match(schema, /"Song" \| "Character"/);

  assert.match(schema, /"\/api\/projects\/\{projectId\}\/analysis\/"/);
  assert.match(schema, /VocalActivityResponse/);
  assert.match(schema, /LyricTimingResponse/);
  assert.match(client, /getSongAnalysis/);
  assert.match(client, /applyTranscriptionLyricTiming/);

  assert.match(schema, /"\/api\/library\/items"/);
  assert.match(schema, /"\/api\/library\/assets"/);
  assert.match(schema, /"\/api\/library\/assets\/\{id\}\/preview"/);
  assert.match(schema, /"\/api\/projects\/\{projectId\}\/characters\/states\/"/);
  assert.match(schema, /VisualLibraryResponse/);
  assert.match(schema, /CharacterContinuityLocks/);
  assert.match(schema, /AssetLibraryResponse/);
  assert.match(schema, /ProjectCharacterStateResponse/);
  assert.match(client, /listVisualLibrary/);
  assert.match(client, /createVisualLibraryItem/);
  assert.match(client, /deleteVisualLibraryItem/);
  assert.match(client, /uploadAssetLibrary/);
  assert.match(client, /getAssetPreviewUrl/);
  assert.match(client, /saveProjectCharacterState/);

  assert.match(client, /paths\["\/api\/providers\/"\]/);
  assert.match(schema, /ProviderCatalogResponse/);
  assert.match(client, /paths\["\/api\/jobs\/"\]/);
  assert.match(schema, /JobState/);
  assert.match(schema, /"\/api\/jobs\/events"/);
});
