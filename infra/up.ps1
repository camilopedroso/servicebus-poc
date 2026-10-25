# Cria (ou atualiza) a infraestrutura da POC e grava o endpoint para os apps usarem.
# Uso: .\infra\up.ps1               lock de 30 s (comportamento normal)
#      .\infra\up.ps1 -ModoDebug    lock de 5 min (máximo), para depurar parado em breakpoints
# (Não é -Debug: esse nome é um parâmetro comum reservado do PowerShell.)
param(
    [string]$ResourceGroup = 'rg-servicebus-poc',
    [string]$Location = 'brazilsouth',
    [switch]$ModoDebug
)

$lockDuration = if ($ModoDebug) { 'PT5M' } else { 'PT30S' }

$ErrorActionPreference = 'Stop'

$principalId = az ad signed-in-user show --query id -o tsv
if (-not $principalId) { throw 'Faça login antes: az login' }

Write-Host "Criando resource group $ResourceGroup em $Location..."
az group create -n $ResourceGroup -l $Location --tags projeto=servicebus-poc -o none

Write-Host "Aplicando main.bicep com lockDuration=$lockDuration (leva ~1 min)..."
$outputs = az deployment group create `
    -g $ResourceGroup `
    -f "$PSScriptRoot\main.bicep" `
    -p developerPrincipalId=$principalId lockDuration=$lockDuration `
    --query properties.outputs -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Deploy falhou.' }

$fqdn = $outputs.fullyQualifiedNamespace.value

# Os três apps leem este arquivo (ignorado pelo git). O nome ficou da fase 1; desde a fase 5 ele também
# leva a connection string do Application Insights, que só funciona com token Entra ID (DisableLocalAuth).
$settingsPath = Join-Path $PSScriptRoot '..\src\servicebus.local.json'
@{
    ServiceBus = @{ FullyQualifiedNamespace = $fqdn }
    ApplicationInsights = @{ ConnectionString = $outputs.appInsightsConnectionString.value }
} |
    ConvertTo-Json -Depth 3 |
    Set-Content -Path $settingsPath -Encoding utf8

Write-Host ''
Write-Host "Pronto: $fqdn"
Write-Host 'Obs.: a permissão RBAC pode levar alguns minutos para propagar (erro 401/Unauthorized no início é normal).'
Write-Host 'Lembre de rodar .\infra\down.ps1 ao terminar para parar a cobrança (~US$ 0,013/h).'
