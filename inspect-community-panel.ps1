$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$bridge = Join-Path $root "bridge_command.py"
if (-not (Test-Path -LiteralPath $bridge)) {
    throw "bridge_command.py não foi encontrado na pasta do pacote."
}

$python = $null
$candidates = @(
    (Join-Path $root "Launcher\Runtime\Python\python.exe"),
    (Join-Path $root "Runtime\Python\python.exe"),
    (Join-Path $root "python.exe")
)
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) { $python = $candidate; break }
}
if (-not $python) {
    $command = Get-Command python -ErrorAction SilentlyContinue
    if ($command) { $python = $command.Source }
}
if (-not $python) {
    throw "Python não encontrado. Instale/abra o runtime portátil do launcher ou adicione Python ao PATH."
}

$pidFile = Join-Path $root "netplay-pid.txt"
if (-not (Test-Path -LiteralPath $pidFile)) {
    throw "O bridge ainda não criou netplay-pid.txt. Abra o jogo pelo launcher e aguarde o carregamento."
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$report = Join-Path $root "CommunityPanel-Inspection-$stamp.txt"
"Revival Revolution - inspeção somente leitura" | Set-Content -LiteralPath $report -Encoding UTF8
"Data: $(Get-Date -Format o)" | Add-Content -LiteralPath $report -Encoding UTF8
"Bridge PID: $((Get-Content -LiteralPath $pidFile -Raw).Trim())" | Add-Content -LiteralPath $report -Encoding UTF8
"" | Add-Content -LiteralPath $report -Encoding UTF8

Push-Location $root
try {
    & $python $bridge inspect 2>&1 | Tee-Object -FilePath $report -Append
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
Write-Host ""
Write-Host "Relatório salvo em: $report"
if ($exitCode -ne 0) {
    Write-Warning "A inspeção retornou código $exitCode. Envie o relatório completo para análise."
    exit $exitCode
}
Write-Host "Inspeção concluída. Envie o relatório para a próxima etapa."
