# Apaga toda a infraestrutura da POC (para a cobrança). Mensagens nas filas são perdidas.
# Uso: .\infra\down.ps1
param(
    [string]$ResourceGroup = 'rg-servicebus-poc'
)

$ErrorActionPreference = 'Stop'

if ((az group exists -n $ResourceGroup) -ne 'true') {
    Write-Host "Resource group $ResourceGroup não existe. Nada a fazer."
    return
}

Write-Host "Apagando $ResourceGroup..."
az group delete -n $ResourceGroup --yes
Write-Host 'Pronto. Nenhum custo de Service Bus a partir de agora.'
