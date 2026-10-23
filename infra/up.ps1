# Cria (ou atualiza) a infraestrutura da POC e grava o endpoint para os apps usarem.
# Uso: .\infra\up.ps1
param(
    [string]$ResourceGroup = 'rg-servicebus-poc',
    [string]$Location = 'brazilsouth'
)

$ErrorActionPreference = 'Stop'

$principalId = az ad signed-in-user show --query id -o tsv
if (-not $principalId) { throw 'Faça login antes: az login' }

Write-Host "Criando resource group $ResourceGroup em $Location..."
az group create -n $ResourceGroup -l $Location --tags projeto=servicebus-poc -o none

Write-Host 'Aplicando main.bicep (leva ~1 min)...'
$outputs = az deployment group create `
    -g $ResourceGroup `
    -f "$PSScriptRoot\main.bicep" `
    -p developerPrincipalId=$principalId `
    --query properties.outputs -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Deploy falhou.' }

$fqdn = $outputs.fullyQualifiedNamespace.value

# Producer e consumer leem este arquivo (ignorado pelo git).
$settingsPath = Join-Path $PSScriptRoot '..\src\servicebus.local.json'
@{ ServiceBus = @{ FullyQualifiedNamespace = $fqdn } } |
    ConvertTo-Json -Depth 3 |
    Set-Content -Path $settingsPath -Encoding utf8

Write-Host ''
Write-Host "Pronto: $fqdn"
Write-Host 'Obs.: a permissão RBAC pode levar alguns minutos para propagar (erro 401/Unauthorized no início é normal).'
Write-Host 'Lembre de rodar .\infra\down.ps1 ao terminar para parar a cobrança (~US$ 0,013/h).'
