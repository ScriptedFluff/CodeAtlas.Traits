param(
    [string]$Configuration = "Release"
)

# ── 1. Declarations ──
$ErrorActionPreference = "Stop"
$rootDir               = $null
$publishDir            = $null
$serverOut             = $null
$cliOut                = $null
$mcpProjectPath        = $null
$cliProjectPath        = $null

function Invoke-StopRunningProcesses {
    # ── 1. Declarations ──
    $processNames = @("CodeAtlas.Traits*", "codeatlas-traits")

    # ── 3. Intent-First Narrative ──
    Get-Process -Name $processNames -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Start-Sleep -Milliseconds 600
}

function Copy-PublishedCliAliases {
    param(
        [Parameter(Mandatory)] [string]$CliOutputDirectory,
        [Parameter(Mandatory)] [string]$RootPublishDirectory
    )

    # ── 1. Declarations ──
    $sourceExe = Join-Path $CliOutputDirectory "CodeAtlas.Traits.exe"
    $sourcePdb = Join-Path $CliOutputDirectory "CodeAtlas.Traits.pdb"

    # ── 2. Preconditions & Guard Clauses ──
    if (-not (Test-Path $sourceExe)) {
        return
    }

    # ── 3. Intent-First Narrative ──
    Copy-Item -Path "$CliOutputDirectory\*" -Destination $RootPublishDirectory -Recurse -Force
    Copy-Item -Path $sourceExe -Destination (Join-Path $CliOutputDirectory "CodeAtlas.Traits.Cli.exe") -Force
    Copy-Item -Path $sourceExe -Destination (Join-Path $RootPublishDirectory "CodeAtlas.Traits.Cli.exe") -Force

    if (Test-Path $sourcePdb) {
        Copy-Item -Path $sourcePdb -Destination (Join-Path $CliOutputDirectory "CodeAtlas.Traits.Cli.pdb") -Force
        Copy-Item -Path $sourcePdb -Destination (Join-Path $RootPublishDirectory "CodeAtlas.Traits.Cli.pdb") -Force
    }
}

# ── 2. Preconditions & Guard Clauses ──
$rootDir        = (Resolve-Path "$PSScriptRoot/..").Path
$publishDir     = [System.IO.Path]::GetFullPath((Join-Path $rootDir "publish"))
$serverOut      = Join-Path $publishDir "server"
$cliOut         = Join-Path $publishDir "cli"
$mcpProjectPath = Join-Path $rootDir "src\CodeAtlas.Traits.Mcp\CodeAtlas.Traits.Mcp.csproj"
$cliProjectPath = Join-Path $rootDir "src\CodeAtlas.Traits.Cli\CodeAtlas.Traits.Cli.csproj"

if (-not (Test-Path $mcpProjectPath)) {
    throw "Could not find MCP server project at '$mcpProjectPath'."
}

if (-not (Test-Path $cliProjectPath)) {
    throw "Could not find CLI project at '$cliProjectPath'."
}

# ── 3. Intent-First Narrative ──
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "  Publishing CodeAtlas.Traits ($Configuration)" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

Invoke-StopRunningProcesses

Write-Host "`n[1/2] Publishing CodeAtlas.Traits.Mcp (MCP Server)..." -ForegroundColor Green
dotnet publish $mcpProjectPath -c $Configuration -o $serverOut

Write-Host "`n[2/2] Publishing CodeAtlas.Traits (CLI)..." -ForegroundColor Green
dotnet publish $cliProjectPath -c $Configuration -o $cliOut

Copy-PublishedCliAliases -CliOutputDirectory $cliOut -RootPublishDirectory $publishDir

# ── 4. Output / Return ──
Write-Host "`n===============================================" -ForegroundColor Green
Write-Host "  Publish Complete!" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green
Write-Host "MCP Server Executable : $serverOut\CodeAtlas.Traits.Mcp.exe" -ForegroundColor White
Write-Host "CLI Executables       : $cliOut\CodeAtlas.Traits.exe" -ForegroundColor White
Write-Host "                        $publishDir\CodeAtlas.Traits.exe" -ForegroundColor White
