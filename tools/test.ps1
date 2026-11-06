<#
.SYNOPSIS
  Runs the test suite in tiers (see docs/testing.md).

.EXAMPLE
  tools\test.ps1            # fast: every test under 3 s, about 30 s with the build
  tools\test.ps1 area HoleSim
  tools\test.ps1 full       # everything, before a commit or release
  tools\test.ps1 slow       # only the slow end-to-end tests

  Areas: Core, HoleSim, Products, Readiness, Water.
#>
param(
    [ValidateSet("fast", "area", "full", "slow")] [string] $Mode = "fast",
    [ValidateSet("Core", "HoleSim", "Products", "Readiness", "Water")] [string] $Area
)

$project = Join-Path $PSScriptRoot "..\tests\Pga2K25.Tests"
$filter = switch ($Mode) {
    "fast" { "Speed!=Slow" }
    "slow" { "Speed=Slow" }
    "area" {
        if (-not $Area) { throw "area needs an area name: Core, HoleSim, Products, Readiness or Water" }
        "Area=$Area"
    }
    default { $null }
}

$dotnetArgs = @("test", $project, "-c", "Release", "-nologo")
if ($filter) { $dotnetArgs += @("--filter", $filter) }
Write-Host "dotnet $($dotnetArgs -join ' ')"
& dotnet @dotnetArgs
exit $LASTEXITCODE
