<#
.SYNOPSIS
    Starts Atlas locally: infrastructure, database setup, and the three services.

.DESCRIPTION
    1. docker compose up for SQL Server, Seq and Azurite (platform/docker-compose.yml)
    2. builds the solution
    3. runs Atlas.DbSetup, retrying while SQL Server finishes starting
    4. starts the stand-ins (:5300), the Onboarding API (:5100) and the Review API (:5200)
    Press Ctrl+C to stop the services. The containers keep running; stop them with
    "docker compose -f platform/docker-compose.yml down".

.PARAMETER SkipInfrastructure
    Do not run docker compose, for when the containers are already up.
#>
param([switch]$SkipInfrastructure)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipInfrastructure) {
    Write-Host "Starting SQL Server, Seq and Azurite..." -ForegroundColor Cyan
    docker compose -f platform/docker-compose.yml up -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed. Is Docker running?" }
}

Write-Host "Building..." -ForegroundColor Cyan
dotnet build Atlas.sln -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

Write-Host "Creating the market databases and service logins..." -ForegroundColor Cyan
$ready = $false
for ($attempt = 1; $attempt -le 30 -and -not $ready; $attempt++) {
    dotnet run --project src/Atlas.DbSetup --no-build
    $ready = $LASTEXITCODE -eq 0
    if (-not $ready) {
        Write-Host "SQL Server is not ready yet, retrying in 5 seconds ($attempt/30)..."
        Start-Sleep -Seconds 5
    }
}
if (-not $ready) { throw "Database setup did not succeed." }

# The services are started from their build output rather than with "dotnet run", so that
# stopping this script stops them too.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$services = @(
    @{ Name = 'Stubs';          Project = 'src/Atlas.Stubs';                     Port = 5300 },
    @{ Name = 'Onboarding API'; Project = 'src/Onboarding/Atlas.Onboarding.Api'; Port = 5100 },
    @{ Name = 'Review API';     Project = 'src/Atlas.Review.Api';                Port = 5200 }
)
$processes = @()
try {
    foreach ($service in $services) {
        $dll = Join-Path 'bin/Debug/net8.0' ((Split-Path $service.Project -Leaf) + '.dll')
        $env:ASPNETCORE_URLS = "http://localhost:$($service.Port)"
        $processes += Start-Process dotnet -ArgumentList $dll -WorkingDirectory $service.Project `
            -NoNewWindow -PassThru
    }

    foreach ($service in $services) {
        $healthy = $false
        for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
            try {
                Invoke-RestMethod "http://localhost:$($service.Port)/health" | Out-Null
                $healthy = $true
            } catch {
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $healthy) { throw "$($service.Name) did not start." }
    }

    Write-Host ""
    Write-Host "Atlas is running:" -ForegroundColor Green
    Write-Host "  Onboarding API  http://localhost:5100/swagger"
    Write-Host "  Review API      http://localhost:5200/swagger"
    Write-Host "  Stand-ins       http://localhost:5300/swagger"
    Write-Host "  Logs (Seq)      http://localhost:5341"
    Write-Host "Try:  curl.exe -s -X POST http://localhost:5100/applications ``"
    Write-Host "        -H 'Content-Type: application/json' --data-binary '@samples/approved-mb.json'"
    Write-Host "Press Ctrl+C to stop."
    Wait-Process -Id ($processes | ForEach-Object Id)
}
finally {
    $processes | Stop-Process -Force -ErrorAction SilentlyContinue
}
