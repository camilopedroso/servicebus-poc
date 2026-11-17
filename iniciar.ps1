# Sobe o ambiente local: SQL Server (Docker), Propostas, Subscrição, Cobrança, Notificação e Operação.
# O container do SQL Server continua de pé depois do Ctrl+C e do -Parar (é local, não custa); para removê-lo: infra\sql-down.ps1.
# Uso: .\iniciar.ps1                  compila e sobe os 5 serviços
#      .\iniciar.ps1 -ComInfra        antes, cria/atualiza a infra no Azure (infra\up.ps1)
#      .\iniciar.ps1 -ComInfra -ModoDebug   idem, com lock de 5 min (máximo) para parar em breakpoints sem perder a mensagem
#      .\iniciar.ps1 -ComAspire       também manda a telemetria para o Aspire Dashboard local (Docker), em http://localhost:18888
#      .\iniciar.ps1 -Parar           encerra processos que ficaram para trás (ex.: janela fechada no X)
# Ctrl+C encerra tudo. A saída de cada app fica em logs\<app>.log; traces e logs correlacionados, no Aspire ou no App Insights.
# No prompt do Claude: ! pwsh -File iniciar.ps1
param(
    [switch]$ComInfra,
    [switch]$ModoDebug,
    [switch]$ComAspire,
    [switch]$Parar
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$apps = @(
    @{ Nome = 'Propostas'; Porta = 5180; Pasta = 'src\Propostas\Vida.Propostas' },
    @{ Nome = 'Subscricao'; Porta = 5181; Pasta = 'src\Subscricao\Vida.Subscricao' },
    @{ Nome = 'Cobranca'; Porta = 5182; Pasta = 'src\Cobranca\Vida.Cobranca' },
    @{ Nome = 'Notificacao'; Porta = 5183; Pasta = 'src\Notificacao\Vida.Notificacao' },
    @{ Nome = 'Operacao'; Porta = 5184; Pasta = 'src\Operacao\Vida.Operacao' }
)
$todasAsPortas = @($apps.Porta)

function Parar-Tudo {
    # Pelo nome do executável: matar só o "dotnet run" deixaria o app filho vivo segurando a porta.
    Get-Process -Name ($apps | ForEach-Object { "Vida.$($_.Nome)" }) -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Porta-Em-Uso([int]$porta) {
    # Tenta conectar em vez de listar as portas: o Get-NetTCPConnection às vezes mostra como CLOSED uma porta que
    # responde. "localhost" tenta IPv6 e IPv4.
    $cliente = [System.Net.Sockets.TcpClient]::new()
    try {
        $conexao = $cliente.ConnectAsync('localhost', $porta)
        $conectou = $conexao.Wait(500)
        return $conectou -and $cliente.Connected
    }
    catch {
        return $false
    }
    finally {
        $cliente.Dispose()
    }
}

if ($Parar) {
    Parar-Tudo
    Write-Host 'Processos encerrados.'
    return
}

# 1) Infra
if ($ModoDebug -and -not $ComInfra) {
    throw '-ModoDebug só vale junto com -ComInfra (o lock é configuração da entidade no broker). Com a infra já no ar: .\infra\up.ps1 -ModoDebug'
}
if ($ComInfra) {
    & "$raiz\infra\up.ps1" -ModoDebug:$ModoDebug
}
if (-not (Test-Path "$raiz\src\servicebus.local.json")) {
    throw 'src\servicebus.local.json não existe. Rode .\iniciar.ps1 -ComInfra (ou .\infra\up.ps1) primeiro.'
}
Write-Host 'Conferindo a infra no Azure...'
if ((az group exists -n rg-servicebus-poc) -ne 'true') {
    throw 'O resource group rg-servicebus-poc não existe. Rode .\iniciar.ps1 -ComInfra.'
}

# 1b) SQL Server local. Obrigatório: Propostas e Subscrição não sobem sem banco (fail fast).
& "$raiz\infra\sql-up.ps1"

# 2) Portas livres
$ocupadas = $todasAsPortas | Where-Object { Porta-Em-Uso $_ }
if ($ocupadas) {
    throw "Porta(s) em uso: $($ocupadas -join ', '). Já está rodando? Use .\iniciar.ps1 -Parar e tente de novo."
}

# 3) Build (uma vez para a solução; os apps sobem a partir dos binários)
# Entre squads, o código chega como pacote do feed local: num clone novo (ou depois de subir uma <Version>), publica antes.
# Idempotente: o que já está no feed fica como está.
& "$raiz\publicar-pacotes.ps1"
Write-Host 'Compilando...'
dotnet build "$raiz\ServiceBusPoc.slnx" --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'Build falhou.' }

# 4) Sobe os processos, com a saída de cada um em logs\
$pastaDeLogs = New-Item -ItemType Directory -Force "$raiz\logs"
$processos = @()
# Mesmo ambiente que o launchSettings.json usa no dotnet run (os processos filhos herdam).
$env:DOTNET_ENVIRONMENT = 'Development'
if ($ComAspire) {
    # Fase 5: o App Insights leva minutos para mostrar um trace; o Aspire Dashboard mostra na hora (OTLP).
    # Anônimo só porque é local. A distro detecta o endpoint e exporta para os dois destinos.
    if (-not (docker ps -q --filter 'name=^vida-aspire$')) {
        docker rm -f vida-aspire 2>$null | Out-Null
        docker run -d --name vida-aspire -p 18888:18888 -p 4317:18889 `
            -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard:latest | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Não consegui subir o Aspire Dashboard (o Docker está rodando?).' }
    }
    $env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
}
try {
    foreach ($app in $apps) {
        # Sobe o executável direto (não "dotnet run"): assim o PID guardado é o do app, e o Ctrl+C o encerra de fato.
        $pastaDoBinario = "$raiz\$($app.Pasta)\bin\Debug\net10.0"
        $processos += Start-Process -FilePath "$pastaDoBinario\Vida.$($app.Nome).exe" `
            -WorkingDirectory $pastaDoBinario -NoNewWindow -PassThru `
            -RedirectStandardOutput "$pastaDeLogs\$($app.Nome).log" -RedirectStandardError "$pastaDeLogs\$($app.Nome).erro.log"
    }

    # 5) Espera todos responderem
    Write-Host 'Subindo Propostas (:5180), Subscricao (:5181), Cobranca (:5182), Notificacao (:5183) e Operacao (:5184)...'
    $limite = (Get-Date).AddSeconds(60)
    while (($todasAsPortas | Where-Object { -not (Porta-Em-Uso $_) }) -and (Get-Date) -lt $limite) {
        if ($processos | Where-Object HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    $caidos = $processos | Where-Object HasExited
    if ($caidos -or ($todasAsPortas | Where-Object { -not (Porta-Em-Uso $_) })) {
        throw "Nem tudo subiu. Veja os arquivos em $pastaDeLogs."
    }

    Write-Host ''
    Write-Host 'Tudo no ar. Interfaces das APIs: http://localhost:5180/scalar (Propostas), :5181/scalar (Subscrição), :5184/scalar (Operação).' -ForegroundColor Green
    Write-Host 'Exemplos de chamadas em requests\*.http.'
    if ($ComAspire) { Write-Host 'Traces, logs e métricas no Aspire Dashboard: http://localhost:18888' -ForegroundColor Green }
    Write-Host 'A primeira chamada ao Service Bus pode levar ~20 s (DefaultAzureCredential procurando o login do az).'
    Write-Host 'Ctrl+C encerra tudo. Ao terminar: .\infra\down.ps1 (a infra cobra ~US$ 0,013/h).'

    # 6) Fica de olho: se um app cair, avisa
    $avisados = @{}
    while ($true) {
        foreach ($p in $processos | Where-Object HasExited) {
            if (-not $avisados[$p.Id]) {
                Write-Host "Um processo encerrou (PID $($p.Id), código $($p.ExitCode)). Veja $pastaDeLogs." -ForegroundColor Yellow
                $avisados[$p.Id] = $true
            }
        }
        Start-Sleep -Seconds 2
    }
}
finally {
    # Roda no Ctrl+C e em qualquer erro acima: não deixa processo segurando porta.
    Write-Host 'Encerrando...'
    $processos | Where-Object { -not $_.HasExited } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Parar-Tudo
}
