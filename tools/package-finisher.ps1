<#
.SYNOPSIS
    Builds a standalone zip of the Course Finisher port for the original developer.

.DESCRIPTION
    The zip holds only the finisher: ready-to-run Windows builds (CourseFinisher.exe desktop app and
    course-finisher.exe command line), the source of the five projects it needs as their own solution,
    the Python-compatibility tests, and the documentation. The staged solution is built and tested
    before zipping, so the package is known to work without the rest of this repo.

.PARAMETER SelfContained
    Bundle the .NET runtime into the executables (bigger, but no .NET 8 install needed).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/package-finisher.ps1
#>
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$commit = (git -C $root rev-parse --short HEAD 2>$null)
if (-not $commit) { $commit = 'local' }
$name = "Pga2K25-CourseFinisher-port-of-v0.10-REV10E-$commit"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
$zip = Join-Path $dist "$name.zip"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

function Copy-Project([string]$relative, [string]$target) {
    $src = Join-Path $root $relative
    $dst = Join-Path $stage $target
    New-Item -ItemType Directory -Force $dst | Out-Null
    Get-ChildItem $src -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            $rel = $_.FullName.Substring($src.Length).TrimStart('\')
            $out = Join-Path $dst $rel
            New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
            Copy-Item $_.FullName $out
        }
}

# --- source: the finisher and what it depends on, nothing else ---------------------------------
$projects = 'Pga2K25.CourseFormat', 'Pga2K25.Finisher', 'Pga2K25.Finisher.Desktop', 'Pga2K25.Finisher.Cli', 'Pga2K25.FinisherApp'
foreach ($p in $projects) { Copy-Project "src\$p" "source\src\$p" }

# The finisher's InternalsVisibleTo points at Pga2K25.Tests, so the packaged test project keeps that name.
$tests = Join-Path $stage 'source\tests\Pga2K25.Tests'
New-Item -ItemType Directory -Force $tests | Out-Null
Copy-Item (Join-Path $root 'tests\Pga2K25.Tests\FinisherCompatTests.cs') $tests
Set-Content -Encoding utf8 (Join-Path $tests 'Pga2K25.Tests.csproj') @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Pga2K25.Finisher\Pga2K25.Finisher.csproj" />
  </ItemGroup>

</Project>
'@

$docs = Join-Path $stage 'source\docs'
New-Item -ItemType Directory -Force $docs | Out-Null
Copy-Item (Join-Path $root 'docs\course-finisher.md') $docs
Copy-Item (Join-Path $root 'docs\course-format\pga2k25-course-format.md') $docs

Push-Location (Join-Path $stage 'source')
try {
    dotnet new sln -n Pga2K25.CourseFinisher | Out-Null
    foreach ($p in $projects) { dotnet sln add "src\$p\$p.csproj" | Out-Null }
    dotnet sln add 'tests\Pga2K25.Tests\Pga2K25.Tests.csproj' | Out-Null

    # --- prove the extracted solution stands on its own -------------------------------------------
    dotnet build -c Release -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Packaged solution failed to build.' }
    dotnet test -c Release --no-build -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Packaged tests failed.' }

    # --- ready-to-run executables -----------------------------------------------------------------
    $bin = Join-Path $stage 'bin'
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    foreach ($p in 'Pga2K25.FinisherApp', 'Pga2K25.Finisher.Cli') {
        dotnet publish "src\$p\$p.csproj" -c Release -r win-x64 --self-contained $sc -o $bin -v q --nologo `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
        if ($LASTEXITCODE -ne 0) { throw "Publish of $p failed." }
    }
    Get-ChildItem $bin -Filter *.pdb | Remove-Item -Force   # SkiaSharp ships a ~100 MB native symbol file
    Get-ChildItem 'src' -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force
    Get-ChildItem 'tests' -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force
}
finally {
    Pop-Location
}

$runtime = if ($SelfContained) { 'The executables include the .NET runtime; nothing else to install.' }
           else { 'Needs the .NET 8 Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/8.0' }
Set-Content -Encoding utf8 (Join-Path $stage 'README.md') @"
# Course Finisher — C# port of v0.10 REV10E

A C# / .NET 8 rewrite of *Mal's TGC 2K25 Course Finisher* v0.10 REV10E, made from the Python in the
released executable. Each pass is a one-to-one translation; the C# doc comments name the Python
function each method replaces. Build: $commit.

## Run it

- **bin\CourseFinisher.exe** — the desktop app: the original form's four option groups, Analyze Course and Create Finished Course.
- **bin\course-finisher.exe** — command line:

      course-finisher analyze "my course.course"
      course-finisher finish "my course.course" --heavy-rough --pins repair --ultra-safe more_safe --weathering

  Run it without arguments for every option. Output defaults to "<course> - Finished.course" next to the
  input; the input is never overwritten.

$runtime

The terrain passes need the fine-terrain lattice that the TGC Tool writes on import, just as in the Python.

## Read first

**source\docs\course-finisher.md** maps every Python function to its C# counterpart, and lists the
deliberate differences (no global 3 dp rounding on save, a few bug fixes) and findings from the port:
28 unreachable functions, the duplicate ``_polygon_area``, and re-run effects of the pass order.

## Interoperability

Both tools read and write the same ``malificaCourseFinisher`` record, with byte-identical tail signatures,
weathering keys and weathering random numbers. Either tool can re-run over the other's output.

## Source

``source\Pga2K25.CourseFinisher.sln``:

| Project | |
|---|---|
| src\Pga2K25.CourseFormat | .course codec (lossless load / save) |
| src\Pga2K25.Finisher | every finishing pass, analysis and the orchestrator (``CourseFinisher.Finish``) |
| src\Pga2K25.Finisher.Desktop | the WPF form as a reusable control |
| src\Pga2K25.FinisherApp | the desktop app |
| src\Pga2K25.Finisher.Cli | the command line |
| tests\Pga2K25.Tests | Python byte-compatibility and numeric-helper tests |

    dotnet build source\Pga2K25.CourseFinisher.sln
    dotnet test source\Pga2K25.CourseFinisher.sln

Not yet done: a side-by-side comparison with the Python tool on a real LiDAR-imported course.
"@

if (Test-Path $zip) { Remove-Item -Force $zip }
# Compress-Archive in Windows PowerShell writes '\' separators, which some unzip tools mangle.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $entry = $name + '/' + $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, 'Optimal') | Out-Null
    }
}
finally {
    $archive.Dispose()
}
Remove-Item -Recurse -Force $stage
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Package: $zip ($size MB)"
