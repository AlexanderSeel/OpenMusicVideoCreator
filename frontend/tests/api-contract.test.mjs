import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const clientPath = new URL("../src/api/client.ts", import.meta.url);
const schemaPath = new URL("../src/api/schema.d.ts", import.meta.url);

test("frontend keeps typed project, analysis, provider, and job API contracts", async () => {
  const [client, schema] = await Promise.all([
    readFile(clientPath, "utf8"),
    readFile(schemaPath, "utf8"),
  ]);

  assert.match(client, /paths\["\/api\/system\/version"\]/);
  assert.match(schema, /SystemVersionResponse/);
  assert.match(schema, /"\/api\/projects\/"/);
  assert.match(schema, /ProjectUpsertRequest/);
  assert.match(client, /listProjects/);
  assert.match(client, /createProject/);
  assert.match(client, /updateProject/);
  assert.match(client, /deleteProject/);
  assert.match(client, /uploadProjectSong/);
  assert.match(schema, /"\/api\/projects\/\{id\}\/song"/);
  assert.match(schema, /ProjectSongResponse/);
  assert.match(schema, /"Song" \| "Character"/);

  assert.match(schema, /"\/api\/projects\/\{projectId\}\/analysis\/"/);
  assert.match(schema, /"\/api\/projects\/\{projectId\}\/analysis\/versions"/);
  assert.match(schema, /"\/api\/projects\/\{projectId\}\/analysis\/sections"/);
  assert.match(schema, /"\/api\/projects\/\{projectId\}\/analysis\/lyrics\/timing"/);
  assert.match(schema, /SongAnalysisResponse/);
  assert.match(schema, /SongSectionRequest/);
  assert.match(schema, /WaveformBucketResponse/);
  assert.match(schema, /VocalActivityResponse/);
  assert.match(schema, /LyricTimingResponse/);
  assert.match(schema, /TranscriptionSegmentRequest/);
  assert.match(client, /getSongAnalysis/);
  assert.match(client, /analyzeSong/);
  assert.match(client, /listSongAnalysisVersions/);
  assert.match(client, /updateSongAnalysisSections/);
  assert.match(client, /getLyricTiming/);
  assert.match(client, /applyTranscriptionLyricTiming/);
  assert.match(client, /listLyricTimingVersions/);

  assert.match(client, /paths\["\/api\/providers\/"\]/);
  assert.match(schema, /ProviderCatalogResponse/);
  assert.match(schema, /ProviderCapability/);
  assert.match(client, /paths\["\/api\/jobs\/"\]/);
  assert.match(schema, /JobState/);
  assert.match(schema, /JobAttemptResponse/);
  assert.match(schema, /"\/api\/jobs\/events"/);
});
