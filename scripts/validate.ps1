$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

npm install
npm run lint
npm run typecheck
npm run test:frontend
npm run build:frontend

dotnet restore backend/OpenMusicVideoCreator.sln
dotnet build backend/OpenMusicVideoCreator.sln -c Release --no-restore
dotnet test backend/OpenMusicVideoCreator.sln -c Release --no-build
