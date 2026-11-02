$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

# O script pode ficar dentro de "Metal Revolution", enquanto bridge_command.py
# fica na raiz do pacote do launcher. Procuramos alguns níveis acima.
$searchRoots = @()
$current = Get-Item -LiteralPath $scriptRoot
for ($i = 0; $i -lt 5 -and $null -ne $current; $i++) {
    $searchRoots += $current.FullName
    $current = $current.Parent
}

$bridge = $null
$packageRoot = $null
foreach ($candidateRoot in $searchRoots) {
    $candidate = Join-Path $candidateRoot "bridge_command.py"
    if (Test-Path -LiteralPath $candidate) {
        $bridge = $candidate
        $packageRoot = $candidateRoot
        break
    }
}
if (-not $bridge) {
    throw "bridge_command.py nao foi encontrado nesta pasta nem nas pastas superiores. Copie o script para dentro da pasta do pacote completo do launcher."
}

# Os arquivos de comando e o PID pertencem a pasta em que o bridge foi
# carregado. Ela pode ser a pasta do jogo, nao a raiz do launcher.
$bridgeRoot = $null
foreach ($candidateRoot in $searchRoots) {
    $pidCandidate = Join-Path $candidateRoot "netplay-pid.txt"
    $dllCandidate = Join-Path $candidateRoot "MetalNetplayBridgeV8.dll"
    if ((Test-Path -LiteralPath $pidCandidate) -and (Test-Path -LiteralPath $dllCandidate)) {
        $bridgeRoot = $candidateRoot
        break
    }
}
if (-not $bridgeRoot) {
    foreach ($candidateRoot in $searchRoots) {
        if (Test-Path -LiteralPath (Join-Path $candidateRoot "netplay-pid.txt")) {
            $bridgeRoot = $candidateRoot
            break
        }
    }
}
if (-not $bridgeRoot) {
    throw "netplay-pid.txt nao foi encontrado. Abra o jogo pelo launcher, aguarde a bridge carregar e execute novamente."
}

$python = $null
$pythonCandidates = @(
    (Join-Path $packageRoot "Launcher\Runtime\Python\python.exe"),
    (Join-Path $packageRoot "Runtime\Python\python.exe"),
    (Join-Path $packageRoot "python.exe"),
    (Join-Path $bridgeRoot "Launcher\Runtime\Python\python.exe"),
    (Join-Path $bridgeRoot "Runtime\Python\python.exe")
)
foreach ($candidate in $pythonCandidates) {
    if (Test-Path -LiteralPath $candidate) {
        $python = $candidate
        break
    }
}
if (-not $python) {
    $command = Get-Command python -ErrorAction SilentlyContinue
    if ($command) { $python = $command.Source }
}
if (-not $python) {
    throw "Python nao encontrado. Abra o runtime portatil do launcher ou adicione Python ao PATH."
}

$pidFile = Join-Path $bridgeRoot "netplay-pid.txt"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$report = Join-Path $scriptRoot "CommunityPanel-Inspection-$stamp.txt"
"Revival Revolution - inspecao somente leitura" | Set-Content -LiteralPath $report -Encoding UTF8
"Data: $(Get-Date -Format o)" | Add-Content -LiteralPath $report -Encoding UTF8
"Bridge root: $bridgeRoot" | Add-Content -LiteralPath $report -Encoding UTF8
"Bridge PID: $((Get-Content -LiteralPath $pidFile -Raw).Trim())" | Add-Content -LiteralPath $report -Encoding UTF8
"Python: $python" | Add-Content -LiteralPath $report -Encoding UTF8
"" | Add-Content -LiteralPath $report -Encoding UTF8

& $python $bridge inspect --root $bridgeRoot 2>&1 | Tee-Object -FilePath $report -Append
$exitCode = $LASTEXITCODE

Write-Host ""
Write-Host "Relatorio salvo em: $report"
if ($exitCode -ne 0) {
    Write-Warning "A inspecao retornou codigo $exitCode. Envie o relatorio completo."
    exit $exitCode
}
Write-Host "Inspecao concluida. Envie o relatorio para analise."
