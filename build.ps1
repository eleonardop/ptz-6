# Compila o PTZ Studio localmente (exige .NET 8 SDK e, para o instalador, Inno Setup 6)
$ErrorActionPreference = "Stop"
dotnet test tests/PtzStudio.Core.Tests/PtzStudio.Core.Tests.csproj -c Release
dotnet publish src/PtzStudio.App/PtzStudio.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) { & $iscc installer\PtzStudio.iss } else { Write-Host "Inno Setup nao encontrado: instalador nao gerado." }
