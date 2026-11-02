$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

# A bridge le os comandos na pasta em que a DLL foi carregada.
# Procuramos a DLL subindo a partir da pasta do jogo/script.
$current = Get-Item -LiteralPath $scriptRoot
$bridgeRoot = $null
for ($i = 0; $i -lt 7 -and $null -ne $current; $i++) {
    $dll = Join-Path $current.FullName "MetalNetplayBridgeV8.dll"
    $pidFile = Join-Path $current.FullName "netplay-pid.txt"
    if ((Test-Path -LiteralPath $dll) -and (Test-Path -LiteralPath $pidFile)) {
        $bridgeRoot = $current.FullName
        break
    }
    $current = $current.Parent
}
if (-not $bridgeRoot) {
    throw "Nao encontrei MetalNetplayBridgeV8.dll junto com netplay-pid.txt nesta pasta ou nas 6 pastas superiores. Abra o jogo pelo launcher e confirme que a DLL da bridge esta instalada."
}

$pidFile = Join-Path $bridgeRoot "netplay-pid.txt"
$targetPidText = (Get-Content -LiteralPath $pidFile -Raw).Trim()
$targetPid = 0
if (-not [int]::TryParse($targetPidText, [ref]$targetPid) -or $targetPid -le 0) {
    throw "netplay-pid.txt existe, mas nao contem um PID valido: '$targetPidText'."
}

# Nao depende de Python nem de bridge_command.py. Reproduz o protocolo
# documentado pelo proprio bridge: grava game-command.json e aguarda
# game-response.json com o mesmo identificador.
$commandPath = Join-Path $bridgeRoot "game-command.json"
$responsePath = Join-Path $bridgeRoot "game-response.json"
$id = [Guid]::NewGuid().ToString("N")
$request = @{
    id = $id
    action = "inspect"
    session_id = "local-test"
    target_pid = $targetPid
    protocol = 1
    slot = 0
    frames = 8
}
$json = $request | ConvertTo-Json -Compress
$tempPath = Join-Path $bridgeRoot ($id + ".tmp")
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($tempPath, $json, $utf8NoBom)
Move-Item -LiteralPath $tempPath -Destination $commandPath -Force

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$report = Join-Path $scriptRoot "CommunityPanel-Inspection-$stamp.txt"
"Revival Revolution - inspecao somente leitura" | Set-Content -LiteralPath $report -Encoding UTF8
"Data: $(Get-Date -Format o)" | Add-Content -LiteralPath $report -Encoding UTF8
"Bridge root: $bridgeRoot" | Add-Content -LiteralPath $report -Encoding UTF8
"Bridge PID: $targetPid" | Add-Content -LiteralPath $report -Encoding UTF8
"Solicitacao: $id" | Add-Content -LiteralPath $report -Encoding UTF8
"" | Add-Content -LiteralPath $report -Encoding UTF8

$response = $null
for ($attempt = 0; $attempt -lt 400; $attempt++) {
    if (Test-Path -LiteralPath $responsePath) {
        try {
            $candidate = Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
            if ($candidate.id -eq $id -and $candidate.state -ne "opening_vs") {
                $response = $candidate
                break
            }
        }
        catch {
            # O bridge pode estar escrevendo a resposta neste instante.
        }
    }
    Start-Sleep -Milliseconds 50
}

if ($null -eq $response) {
    "ERRO: tempo esgotado aguardando resposta da bridge." | Add-Content -LiteralPath $report -Encoding UTF8
    Write-Host "Tempo esgotado. Verifique se o jogo continua aberto e se a bridge foi anexada."
    Write-Host "Relatorio: $report"
    exit 2
}

$responseText = $response | ConvertTo-Json -Depth 10
$responseText | Tee-Object -FilePath $report -Append
Write-Host ""
Write-Host "Relatorio salvo em: $report"
if ($response.state -eq "error" -or $response.state -eq "rejected" -or $response.state -eq "rollback_test_failed") {
    Write-Warning "A bridge respondeu com estado: $($response.state)"
    exit 1
}
Write-Host "Inspecao concluida. Envie o relatorio para analise."
