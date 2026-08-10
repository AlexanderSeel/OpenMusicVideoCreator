[CmdletBinding()]
param(
    [string]$BackendUrl = 'http://localhost:5100',
    [int]$FrontendPort = 3000,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repositoryRoot 'backend/src/OpenMusicVideoCreator.Api/OpenMusicVideoCreator.Api.csproj'
$frontendDirectory = Join-Path $repositoryRoot 'frontend'
$nextCli = Join-Path $repositoryRoot 'node_modules/next/dist/bin/next'
$frontendUrl = "http://localhost:$FrontendPort"

function Assert-CommandAvailable {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found on PATH. Install the prerequisites listed in README.md and try again."
    }
}

function Wait-ForHttpEndpoint {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$ServiceName,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)

    throw "$ServiceName did not become reachable at $Url within $TimeoutSeconds seconds."
}

Assert-CommandAvailable dotnet
Assert-CommandAvailable npm

if (-not (Test-Path -LiteralPath $apiProject)) {
    throw "API project was not found at '$apiProject'. Run this script from an intact repository checkout."
}
if (-not (Test-Path -LiteralPath $nextCli)) {
    throw "Next.js is not installed at '$nextCli'. Run 'npm install' from the repository root and try again."
}

$previousApiBaseUrl = $env:NEXT_PUBLIC_API_BASE_URL
$env:NEXT_PUBLIC_API_BASE_URL = $BackendUrl
$backendProcess = $null
$frontendProcess = $null

try {
    Write-Host "Starting backend at $BackendUrl ..."
    $backendProcess = Start-Process -FilePath dotnet -WorkingDirectory $repositoryRoot -NoNewWindow -PassThru -ArgumentList @(
        'run', '--project', $apiProject, '--urls', $BackendUrl
    )
    Wait-ForHttpEndpoint -Url "$BackendUrl/healthz" -ServiceName 'Backend'

    Write-Host "Starting frontend at $frontendUrl ..."
    $frontendProcess = Start-Process -FilePath node -WorkingDirectory $frontendDirectory -NoNewWindow -PassThru -ArgumentList @(
        $nextCli, 'dev', '--hostname=localhost', "--port=$FrontendPort"
    )
    Wait-ForHttpEndpoint -Url $frontendUrl -ServiceName 'Frontend'

    Write-Host "OpenMusicVideoCreator is running at $frontendUrl"
    Write-Host 'Press Ctrl+C to stop both the frontend and backend.'

    if (-not $NoBrowser) {
        Start-Process $frontendUrl
    }

    Wait-Process -Id $frontendProcess.Id
}
finally {
    foreach ($process in @($frontendProcess, $backendProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    $env:NEXT_PUBLIC_API_BASE_URL = $previousApiBaseUrl
}
