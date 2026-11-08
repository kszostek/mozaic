<#
  Builds Mozaic, runs the unit tests and publishes to .\publish: Mozaic.exe and mozaic-msg.exe (NativeAOT,
  no runtime needed) and MozaicSettings.exe (WPF, needs the .NET 10 Desktop Runtime).
    .\build.ps1                      # build + test + publish
    .\build.ps1 -SkipTests
    .\build.ps1 -Install             # also copy to %LOCALAPPDATA%\Programs\Mozaic
    .\build.ps1 -Version 0.2.0 -Package -SkipDocs
                                     # release build: stamp the version, zip to .\artifacts (used by CI)
  Requires the .NET 10 SDK and Visual Studio (or Build Tools) with "Desktop development with C++" for NativeAOT.
#>
param(
    [switch]$SkipTests,
    [switch]$Install,
    [switch]$Package,     # zip the published exes to artifacts\Mozaic-<version>-<runtime>.zip
    [switch]$SkipDocs,    # don't regenerate the README keyboard images
    [string]$Version,     # default: <Version> in Directory.Build.props
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# NativeAOT's linker lookup needs vswhere.exe on PATH.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }

if (-not $Version) { $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version }
$common = @('-c', 'Release', '-nologo', "-p:Version=$Version")
Write-Host "Mozaic $Version ($Runtime)"

dotnet build Mozaic.sln @common -v q
if ($LASTEXITCODE) { throw 'build failed' }

if (-not $SkipTests) {
    dotnet test tests/Mozaic.Tests @common --no-build
    if ($LASTEXITCODE) { throw 'unit tests failed' }
}

dotnet publish src/Mozaic/Mozaic.csproj @common -r $Runtime -o publish
if ($LASTEXITCODE) { throw 'publish failed' }
dotnet publish src/MozaicMsg/MozaicMsg.csproj @common -r $Runtime -o publish
if ($LASTEXITCODE) { throw 'publish failed' }
# The settings app is WPF (not NativeAOT-capable): a single exe that uses the .NET 10 Desktop Runtime.
dotnet publish src/MozaicSettings/MozaicSettings.csproj @common -r $Runtime --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if ($LASTEXITCODE) { throw 'publish failed' }
Get-ChildItem publish -Filter *.pdb | Remove-Item

if (-not $SkipDocs) {
    # Keyboard cheat-sheet images for the README, generated from the default config.
    $env:MOZAIC_LANG = 'en'
    Start-Process publish\MozaicSettings.exe -ArgumentList '--config', '"docs\no-such-file"', '--render-guide', '"docs\images"' -Wait
    $env:MOZAIC_LANG = $null
}

Write-Host ''
Get-ChildItem publish -Filter *.exe | ForEach-Object { '{0,-16} {1,6:N1} MB' -f $_.Name, ($_.Length / 1MB) }

if ($Package) {
    New-Item -ItemType Directory -Force artifacts | Out-Null
    $zip = Join-Path artifacts "Mozaic-$Version-$Runtime.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path publish\Mozaic.exe, publish\MozaicSettings.exe, publish\mozaic-msg.exe -DestinationPath $zip
    '{0,-16} {1,6:N1} MB' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
}

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA 'Programs\Mozaic'
    $running = Get-Process Mozaic -ErrorAction SilentlyContinue
    if ($running) { & (Join-Path $dest 'mozaic-msg.exe') exit 2>$null; Start-Sleep 2 }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item publish\*.exe $dest -Force
    Write-Host "Installed to $dest  (enable 'Start with Windows' from the tray menu)"
}
