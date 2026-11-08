<#
  Builds Mozaic, runs the unit tests and publishes to .\publish: Mozaic.exe and mozaic-msg.exe (NativeAOT,
  no runtime needed) and MozaicSettings.exe (WPF, needs the .NET 10 Desktop Runtime).
    .\build.ps1              # build + test + publish
    .\build.ps1 -SkipTests
    .\build.ps1 -Install     # also copy to %LOCALAPPDATA%\Programs\Mozaic
  Requires the .NET 10 SDK and Visual Studio (or Build Tools) with "Desktop development with C++" for NativeAOT.
#>
param([switch]$SkipTests, [switch]$Install, [string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# NativeAOT's linker lookup needs vswhere.exe on PATH.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }

dotnet build Mozaic.sln -c Release -nologo -v q
if ($LASTEXITCODE) { throw 'build failed' }

if (-not $SkipTests) {
    dotnet test tests/Mozaic.Tests -c Release --no-build -nologo
    if ($LASTEXITCODE) { throw 'unit tests failed' }
}

dotnet publish src/Mozaic/Mozaic.csproj -c Release -r $Runtime -o publish -nologo
if ($LASTEXITCODE) { throw 'publish failed' }
dotnet publish src/MozaicMsg/MozaicMsg.csproj -c Release -r $Runtime -o publish -nologo
if ($LASTEXITCODE) { throw 'publish failed' }
# The settings app is WPF (not NativeAOT-capable): a single exe that uses the .NET 10 Desktop Runtime.
dotnet publish src/MozaicSettings/MozaicSettings.csproj -c Release -r $Runtime --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish -nologo
if ($LASTEXITCODE) { throw 'publish failed' }
Get-ChildItem publish -Filter *.pdb | Remove-Item

# Keyboard cheat-sheet images for the README, generated from the default config.
$env:MOZAIC_LANG = 'en'
Start-Process publish\MozaicSettings.exe -ArgumentList '--config', '"docs\no-such-file"', '--render-guide', '"docs\images"' -Wait
$env:MOZAIC_LANG = $null

Write-Host ''
Get-ChildItem publish -Filter *.exe | ForEach-Object { '{0,-16} {1,6:N1} MB' -f $_.Name, ($_.Length / 1MB) }

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA 'Programs\Mozaic'
    $running = Get-Process Mozaic -ErrorAction SilentlyContinue
    if ($running) { & (Join-Path $dest 'mozaic-msg.exe') exit 2>$null; Start-Sleep 2 }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item publish\*.exe $dest -Force
    Write-Host "Installed to $dest  (enable 'Start with Windows' from the tray menu)"
}
