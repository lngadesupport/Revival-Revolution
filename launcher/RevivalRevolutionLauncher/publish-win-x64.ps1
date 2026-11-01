$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "RevivalRevolutionLauncher.csproj"
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou com código $LASTEXITCODE" }
Write-Host "Publicação concluída em bin/Release/net8.0-windows/win-x64/publish"
