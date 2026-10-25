# Para e remove o container do SQL Server local.
# Uso: .\infra\sql-down.ps1                 remove o container; os dados ficam no volume
#      .\infra\sql-down.ps1 -ApagarDados    remove também o volume e a connection string (recomeça do zero)
param([switch]$ApagarDados)

$ErrorActionPreference = 'Stop'
$container = 'vida-sql'

docker rm -f $container 2>$null | Out-Null
Write-Host "Container $container removido."

if ($ApagarDados) {
    docker volume rm vida-sql-dados 2>$null | Out-Null
    Remove-Item (Join-Path $PSScriptRoot '..\src\sqlserver.local.json') -ErrorAction SilentlyContinue
    Write-Host 'Volume e connection string apagados.'
}
