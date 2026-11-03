# Run as Administrator on BOTH PCs connected to the same Radmin VPN network.
$ErrorActionPreference = "Stop"
$principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Abra o PowerShell como Administrador e execute este script novamente."
}
$ruleName = "Revival Revolution Radmin P2P UDP 8766"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol UDP -LocalPort 8766 -RemoteAddress "26.0.0.0/8" | Out-Null
Write-Host "Regra UDP 8766 liberada somente para endereços Radmin 26.0.0.0/8."
