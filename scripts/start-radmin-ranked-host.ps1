$ErrorActionPreference = 'Stop'
$Port = 8765
$RuleName = 'Revival Ranked - Radmin VPN only'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DataRoot = Join-Path $env:LOCALAPPDATA 'RevivalRevolution\RankedServer'
$Database = Join-Path $DataRoot 'ranked.sqlite3'

$RadminAddress = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -match '^26\.' -and $_.AddressState -eq 'Preferred' } |
    Sort-Object InterfaceIndex |
    Select-Object -First 1 -ExpandProperty IPAddress

if (-not $RadminAddress) {
    throw 'Nenhum IP Radmin VPN (26.x.x.x) foi encontrado. Conecte-se à rede Radmin e execute novamente.'
}

New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

if (-not (Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)) {
    $command = "New-NetFirewallRule -DisplayName '$RuleName' -Direction Inbound -Action Allow -Protocol TCP -LocalAddress '$RadminAddress' -LocalPort $Port -RemoteAddress '26.0.0.0/8' -Profile Any | Out-Null"
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-Command',$command) | Out-Null
    if (-not (Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)) {
        throw 'A regra de firewall Radmin não foi criada. Execute o script como Administrador.'
    }
}

$pythonLauncher = Get-Command 'py.exe' -ErrorAction SilentlyContinue
$python = Get-Command 'python.exe' -ErrorAction SilentlyContinue
if (-not $pythonLauncher -and -not $python) {
    throw 'Python 3 não foi encontrado. Instale Python 3.10+ e marque Add Python to PATH.'
}

Write-Host ''
Write-Host 'REVIVAL REVOLUTION - RANKED HOST (RADMIN VPN)' -ForegroundColor Cyan
Write-Host "IP Radmin: $RadminAddress"
Write-Host "API:       http://$($RadminAddress):$Port"
Write-Host "Banco:     $Database"
Write-Host 'A API será vinculada somente ao IP Radmin deste computador.' -ForegroundColor Green
Write-Host 'Mantenha esta janela aberta durante as partidas. Ctrl+C encerra o servidor.'
Write-Host ''

Push-Location $RepoRoot
try {
    if ($pythonLauncher) {
        & $pythonLauncher.Source -3 -m ranked_server --host $RadminAddress --port $Port --database $Database
    } else {
        & $python.Source -m ranked_server --host $RadminAddress --port $Port --database $Database
    }
} finally {
    Pop-Location
}