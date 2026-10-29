# Sobe o SQL Server local (Docker) e grava as connection strings dos serviços que têm banco: Propostas (fase 4)
# e Subscrição (etapa 9). Um SQL Server, um banco por serviço: cada um lê só o seu.
# Uso: .\infra\sql-up.ps1
# Roda local de propósito: só o Service Bus fica no Azure (custo). Os dados ficam no volume "vida-sql-dados"
# e sobrevivem a reinícios; .\infra\sql-down.ps1 remove o container (com -ApagarDados, o volume também).
$ErrorActionPreference = 'Stop'

$container = 'vida-sql'
$porta = 14330  # fora da 1433, para não brigar com um SQL Server instalado na máquina
$arquivo = Join-Path $PSScriptRoot '..\src\sqlserver.local.json'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado. Instale o Docker Desktop.' }
docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) { throw 'O Docker não está rodando. Abra o Docker Desktop e tente de novo.' }

$existe = docker ps -a --filter "name=^$container$" --format '{{.Names}}'
if ($existe) {
    if (-not (Test-Path $arquivo)) {
        throw "O container $container existe, mas $arquivo não. Rode .\infra\sql-down.ps1 -ApagarDados e suba de novo."
    }
    Write-Host "Iniciando o container $container..."
    docker start $container | Out-Null
}
else {
    # Senha só desta máquina, gerada uma vez e guardada no arquivo ignorado pelo git (nunca no repositório).
    # A política do SQL Server exige maiúscula, minúscula, número e símbolo.
    $letras = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 20 | ForEach-Object { [char]$_ })
    $senha = "Vd!$letras" + '9a'
    Write-Host "Criando o container $container (SQL Server 2022, porta $porta)..."
    docker run -d --name $container `
        -e 'ACCEPT_EULA=Y' -e "MSSQL_SA_PASSWORD=$senha" -e 'MSSQL_PID=Developer' `
        -p "${porta}:1433" -v vida-sql-dados:/var/opt/mssql `
        mcr.microsoft.com/mssql/server:2022-latest | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker run falhou.' }

    # Os serviços leem este arquivo (como o servicebus.local.json). TrustServerCertificate só porque é local.
    $conexao = "Server=localhost,$porta;Database=VidaPropostas;User Id=sa;Password=$senha;TrustServerCertificate=True;Encrypt=True"
    @{ ConnectionStrings = @{ Propostas = $conexao } } |
        ConvertTo-Json -Depth 3 |
        Set-Content -Path $arquivo -Encoding utf8
}

# Etapa 9: a Subscrição tem o banco dela no mesmo SQL Server. Arquivos criados antes só têm a de Propostas: completa,
# reaproveitando a senha (mesmo servidor, outro banco).
$configuracao = Get-Content $arquivo -Raw | ConvertFrom-Json
if (-not $configuracao.ConnectionStrings.Subscricao) {
    $daSubscricao = $configuracao.ConnectionStrings.Propostas -replace 'Database=VidaPropostas', 'Database=VidaSubscricao'
    $configuracao.ConnectionStrings | Add-Member -NotePropertyName Subscricao -NotePropertyValue $daSubscricao
    $configuracao | ConvertTo-Json -Depth 3 | Set-Content -Path $arquivo -Encoding utf8
    Write-Host 'Connection string da Subscrição acrescentada (recompile para copiar o arquivo).'
}

# O SQL Server leva alguns segundos para aceitar conexões depois que o container sobe.
Write-Host 'Esperando o SQL Server aceitar conexões...'
$limite = (Get-Date).AddSeconds(90)
do {
    Start-Sleep -Seconds 2
    $pronto = (docker logs $container 2>&1 | Select-String 'SQL Server is now ready for client connections' -Quiet)
} until ($pronto -or (Get-Date) -gt $limite)
if (-not $pronto) { throw "O SQL Server não ficou pronto em 90 s. Veja: docker logs $container" }

Write-Host "Pronto: localhost,$porta (bancos VidaPropostas e VidaSubscricao, criados pelos serviços na partida)."
