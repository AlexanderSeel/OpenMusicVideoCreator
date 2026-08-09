#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

npm install
npm run lint
npm run typecheck
npm run test:frontend
npm run build:frontend

dotnet restore backend/OpenMusicVideoCreator.sln
dotnet build backend/OpenMusicVideoCreator.sln -c Release --no-restore
dotnet test backend/OpenMusicVideoCreator.sln -c Release --no-build
