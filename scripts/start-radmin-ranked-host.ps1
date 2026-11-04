$ErrorActionPreference = 'Stop'

# Host local do backend Ranked. A API fica vinculada exclusivamente ao IP Radmin.
# O segredo de resultados competitivos NÃO deve ser configurado neste host.
$ApiPort = 8765
$PeerPort = 8766
$ApiRule = 'Revival Ranked API - Radmin VPN only'
$PeerRule = 'Revival Ranked P2P - Radmin VPN only'
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

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "A configuração do firewall falhou (código $($process.ExitCode))." }
    exit 0
}

New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

# Recria as regras para acompanhar mudanças do IP virtual do adaptador Radmin.
Get-NetFirewallRule -DisplayName $ApiRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName $PeerRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ApiRule -Direction Inbound -Action Allow -Protocol TCP `
    -LocalAddress $RadminAddress -LocalPort $ApiPort -RemoteAddress '26.0.0.0/8' -Profile Any | Out-Null
New-NetFirewallRule -DisplayName $PeerRule -Direction Inbound -Action Allow -Protocol UDP `
    -LocalAddress $RadminAddress -LocalPort $PeerPort -RemoteAddress '26.0.0.0/8' -Profile Any | Out-Null

$pythonLauncher = Get-Command 'py.exe' -ErrorAction SilentlyContinue
$python = Get-Command 'python.exe' -ErrorAction SilentlyContinue
if (-not $pythonLauncher -and -not $python) {
    throw 'Python 3 não foi encontrado. Instale Python 3.10+ e marque Add Python to PATH.'
}

# Never inherit a result-authority secret into this local matchmaking host.
Remove-Item Env:RANKED_RESULT_SECRET -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'REVIVAL REVOLUTION - RANKED HOST (RADMIN VPN)' -ForegroundColor Cyan
Write-Host "IP Radmin: $RadminAddress"
Write-Host "API:       http://$($RadminAddress):$ApiPort"
Write-Host "P2P UDP:   $PeerPort"
Write-Host "Banco:     $Database"
Write-Host 'A API e as regras de entrada ficam limitadas à interface/rede Radmin.' -ForegroundColor Green
Write-Host 'Use este endereço no campo Servidor Ranked de cada launcher na mesma rede Radmin.'
Write-Host 'Mantenha esta janela aberta durante as partidas. Ctrl+C encerra o servidor.'
Write-Host 'O envio de resultados Ranked permanece desativado até existir autoridade confiável.'
Write-Host ''

Push-Location $RepoRoot
try {
    if ($pythonLauncher) {
        & $pythonLauncher.Source -3 -m ranked_server --host $RadminAddress --port $ApiPort --database $Database
    } else {
        & $python.Source -m ranked_server --host $RadminAddress --port $ApiPort --database $Database
    }
} finally {
    Pop-Location
}
