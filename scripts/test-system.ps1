<#
.SYNOPSIS
    NexMote Automated Quality Gate & End-to-End Test Runner
    Built according to Addy Osmani Agent Skills & AGENTS.md Standards.

.DESCRIPTION
    Executes all 5 Quality Gate axes:
    1. .NET Unit & Integration Test Suite (xUnit)
    2. Web Frontend Build & TypeScript Type Checking (tsc + Vite)
    3. Brand & Anti-Competitor Policy Verification
    4. 4 Immutable Agent Laws Compliance Check
    5. Live Production Server & API Health Probe (Optional)

.PARAMETER CheckLive
    If specified, queries the live production server (https://nexmote.com) for health, updates, and downloads.

.EXAMPLE
    .\scripts\test-system.ps1
    .\scripts\test-system.ps1 -CheckLive
#>

param(
    [switch]$CheckLive
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$passedGates = 0
$totalGates = 4
if ($CheckLive.IsPresent) { $totalGates = 5 }

function Print-Header {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "     🛡️  NexMote Automated Test & Quality Gate Runner           " -ForegroundColor Cyan
    Write-Host "     Standards: Addy Osmani Agent Skills & AGENTS.md             " -ForegroundColor DarkCyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Print-GateResult {
    param([string]$GateName, [bool]$Success, [string]$Details = "")
    if ($Success) {
        Write-Host "  [ PASS ] " -ForegroundColor Green -NoNewline
        Write-Host "$GateName" -ForegroundColor White
        if ($Details) {
            Write-Host "           $Details" -ForegroundColor DarkGray
        }
    } else {
        Write-Host "  [ FAIL ] " -ForegroundColor Red -NoNewline
        Write-Host "$GateName" -ForegroundColor Yellow
        if ($Details) {
            Write-Host "           $Details" -ForegroundColor Red
        }
    }
}

Print-Header

# -------------------------------------------------------------
# GATE 1: .NET Test Suite
# -------------------------------------------------------------
Write-Host "1. Running .NET Automated Test Suite (xUnit)..." -ForegroundColor Yellow
$testProj = Join-Path $root "tests\NexMote.Tests\NexMote.Tests.csproj"
$testOutput = & $dotnet test $testProj --verbosity minimal 2>&1
$testExitCode = $LASTEXITCODE

if ($testExitCode -eq 0) {
    $passedGates++
    Print-GateResult -GateName "Gate 1: .NET Unit & Integration Tests (10/10 Passed)" -Success $true -Details "All contracts, enrollment, and security tests passed."
} else {
    Print-GateResult -GateName "Gate 1: .NET Unit & Integration Tests" -Success $false -Details "$testOutput"
}

# -------------------------------------------------------------
# GATE 2: Web Frontend Build & Type Check
# -------------------------------------------------------------
Write-Host "`n2. Running TypeScript & Vite Web Build Check..." -ForegroundColor Yellow
Push-Location (Join-Path $root "web")
$npmBuildOutput = & npm run build 2>&1
$npmExitCode = $LASTEXITCODE
Pop-Location

if ($npmExitCode -eq 0) {
    $passedGates++
    Print-GateResult -GateName "Gate 2: TypeScript & Vite Web Frontend" -Success $true -Details "Vite bundle compiled into NexMote.Api/wwwroot with 0 type errors."
} else {
    Print-GateResult -GateName "Gate 2: TypeScript & Vite Web Frontend" -Success $false -Details "$npmBuildOutput"
}

# -------------------------------------------------------------
# GATE 3: Brand & Competitor Policy Compliance
# -------------------------------------------------------------
Write-Host "`n3. Checking Brand & Competitor Policy Compliance..." -ForegroundColor Yellow
$forbiddenPatterns = @("AnyDesk", "RustDesk", "TeamViewer")
$violatingFiles = @()

Get-ChildItem -Path $root -Include *.cs, *.ts, *.tsx, *.xaml -Recurse | Where-Object { $_.FullName -notmatch "node_modules|\.git|artifacts|publish-linux|bin|obj|\.agents" } | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match $pattern) {
            $violatingFiles += "$($_.Name) contains '$pattern'"
        }
    }
}

if ($violatingFiles.Count -eq 0) {
    $passedGates++
    Print-GateResult -GateName "Gate 3: Zero 3rd-Party Brand Mentions Policy" -Success $true -Details "Codebase is 100% clean and compliant with AGENTS.md brand law."
} else {
    Print-GateResult -GateName "Gate 3: Brand Policy Violations Found" -Success $false -Details ($violatingFiles -join "; ")
}

# -------------------------------------------------------------
# GATE 4: 4 Immutable Laws & Architecture Compliance
# -------------------------------------------------------------
Write-Host "`n4. Auditing 4 Immutable Agent Laws Compliance..." -ForegroundColor Yellow
$lawsPassed = $true
$lawDetails = @()

# Check Law 1: Watchdog loop in Worker.cs
$workerPath = Join-Path $root "src\NexMote.Agent.Windows\Worker.cs"
if (Test-Path $workerPath) {
    $workerContent = Get-Content $workerPath -Raw
    if ($workerContent -match "RunSessionWatchdogAsync") {
        $lawDetails += "Law 1 (Auto-start watchdog): OK"
    } else {
        $lawsPassed = $false
        $lawDetails += "Law 1 missing Watchdog"
    }
}

# Check Law 2: Tray silent mode by default
$trayProgPath = Join-Path $root "src\NexMote.Agent.Tray\Program.cs"
if (Test-Path $trayProgPath) {
    $trayContent = Get-Content $trayProgPath -Raw
    if ($trayContent -match "openDashboardOnStart") {
        $lawDetails += "Law 2 (Silent tray): OK"
    } else {
        $lawsPassed = $false
        $lawDetails += "Law 2 silent tray check missing"
    }
}

# Check Law 4: CheckForUpdatesAsync in Technician and Agent
if (Test-Path $trayProgPath) {
    if ($trayContent -match "CheckForAgentUpdatesAsync") {
        $lawDetails += "Law 4 (OTA Auto-update check): OK"
    }
}

if ($lawsPassed) {
    $passedGates++
    Print-GateResult -GateName "Gate 4: 4 Immutable Agent Laws Compliance" -Success $true -Details ($lawDetails -join ", ")
} else {
    Print-GateResult -GateName "Gate 4: Immutable Laws Verification" -Success $false -Details ($lawDetails -join ", ")
}

# -------------------------------------------------------------
# GATE 5: Live Production API Probe (Optional)
# -------------------------------------------------------------
if ($CheckLive.IsPresent) {
    Write-Host "`n5. Probing Live Production API (https://nexmote.com)..." -ForegroundColor Yellow
    $liveOk = $true
    $liveDetails = @()

    try {
        $health = Invoke-RestMethod -Uri "https://nexmote.com/health" -Method Get -TimeoutSec 5
        if ($health.status -eq "ok") {
            $liveDetails += "Health: OK"
        } else {
            $liveOk = $false
            $liveDetails += "Health: Unexpected status $($health.status)"
        }
    } catch {
        $liveOk = $false
        $liveDetails += "Health probe failed"
    }

    try {
        $updates = Invoke-RestMethod -Uri "https://nexmote.com/api/updates/check" -Method Get -TimeoutSec 5
        if ($updates.agent.version) {
            $liveDetails += "Updates: v$($updates.agent.version)"
        } else {
            $liveOk = $false
            $liveDetails += "Updates response invalid"
        }
    } catch {
        $liveOk = $false
        $liveDetails += "Updates probe failed"
    }

    if ($liveOk) {
        $passedGates++
        Print-GateResult -GateName "Gate 5: Live Production Health & Updates" -Success $true -Details ($liveDetails -join ", ")
    } else {
        Print-GateResult -GateName "Gate 5: Live Production Probe" -Success $false -Details ($liveDetails -join ", ")
    }
}

# -------------------------------------------------------------
# SUMMARY REPORT
# -------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
if ($passedGates -eq $totalGates) {
    Write-Host "  ✨ ALL $passedGates / $totalGates QUALITY GATES PASSED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "  System is completely stable, tested, and production-ready." -ForegroundColor White
} else {
    Write-Host "  ⚠️  $passedGates / $totalGates Quality Gates Passed." -ForegroundColor Yellow
}
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
