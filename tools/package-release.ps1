# Packages Course Designer Studio as a stand-alone Windows zip: a self-contained single-file exe (no .NET install
# needed), the bundled Lora fonts beside it, and the testers' read-me. The version comes from the Studio project.
#
#   powershell -ExecutionPolicy Bypass -File tools\package-release.ps1 [-OutDir <folder>]

param([string]$OutDir = (Join-Path $PSScriptRoot "..\..\releases"))

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repo "src\Pga2K25.Studio\Pga2K25.Studio.csproj"
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "CourseDesignerStudio-$version-win-x64"

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = Resolve-Path $OutDir
$staging = Join-Path $OutDir $name
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

dotnet publish $project -c Release -r win-x64 --self-contained true -o $staging --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# The single-file bundler swallows the font files, and the card renderer looks for them next to the exe. -LiteralPath
# and ZipFile (not Compress-Archive) because "[wght]" in the font names is a wildcard to PowerShell.
$fonts = Join-Path $staging "fonts\lora"
New-Item -ItemType Directory -Force $fonts | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo "assets\fonts\lora") -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $fonts }
Get-ChildItem -LiteralPath $staging -Filter *.pdb | Remove-Item -Force
Copy-Item (Join-Path $repo "docs\release-readme.txt") (Join-Path $staging "README.txt")

$zip = Join-Path $OutDir "$name.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
"Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
