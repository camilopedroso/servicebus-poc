# Sobe tudo para estudar: SQL Server local (Docker), Propostas, Subscrição, Cobrança, Notificação, Operação e o painel,
# e abre o navegador.
# O container do SQL Server continua de pé depois do Ctrl+C e do -Parar (é local, não custa); para removê-lo: infra\sql-down.ps1.
# Uso: .\iniciar.ps1                  compila, sobe os 6 processos e abre http://localhost:5173
#      .\iniciar.ps1 -ComInfra        antes, cria/atualiza a infra no Azure (infra\up.ps1)
#      .\iniciar.ps1 -ComInfra -ModoDebug   idem, com lock de 5 min (máximo) para parar em breakpoints sem perder a mensagem
#      .\iniciar.ps1 -SemNavegador    não abre o navegador
#      .\iniciar.ps1 -ComAspire       também manda a telemetria para o Aspire Dashboard local (Docker), em http://localhost:18888
#      .\iniciar.ps1 -Parar           encerra processos que ficaram para trás (ex.: janela fechada no X)
# Ctrl+C encerra tudo. A saída de cada app fica em logs\<app>.log; o painel mostra os mesmos logs ao vivo.
# No prompt do Claude: ! pwsh -File iniciar.ps1
param(
    [switch]$ComInfra,
    [switch]$ModoDebug,
    [switch]$SemNavegador,
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
$portaDoPainel = 5173
$todasAsPortas = @($apps.Porta) + $portaDoPainel

function Parar-Tudo {
    # Pelo nome do executável: matar só o "dotnet run" deixaria o app filho vivo segurando a porta.
    Get-Process -Name ($apps | ForEach-Object { "Vida.$($_.Nome)" }) -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    # Sem -State Listen: o Vite aparece como CLOSED aqui (ver Porta-Em-Uso).
    Get-NetTCPConnection -LocalPort $portaDoPainel -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
}

function Porta-Em-Uso([int]$porta) {
    # Tenta conectar em vez de listar as portas: neste PC o Get-NetTCPConnection mostra o Vite ([::1]:5173) como
    # CLOSED mesmo respondendo, e o script esperava os 60 s e derrubava tudo. "localhost" tenta IPv6 e IPv4.
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

# 1b) SQL Server local (fase 4). Sem Docker, só avisa: as fases 1 a 3 não dependem dele.
try {
    & "$raiz\infra\sql-up.ps1"
}
catch {
    Write-Host "SQL Server não subiu ($($_.Exception.Message)). A API de propostas (fase 4) vai responder 503." -ForegroundColor Yellow
}

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

$painel = "$raiz\web\painel"
if (-not (Test-Path "$painel\node_modules")) {
    Write-Host 'Instalando dependências do painel (só na primeira vez)...'
    npm install --prefix $painel --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'npm install falhou.' }
}

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
    $processos += Start-Process -FilePath 'node' -ArgumentList 'node_modules/vite/bin/vite.js', '--port', $portaDoPainel, '--strictPort' `
        -WorkingDirectory $painel -NoNewWindow -PassThru `
        -RedirectStandardOutput "$pastaDeLogs\Painel.log" -RedirectStandardError "$pastaDeLogs\Painel.erro.log"

    # 5) Espera todos responderem
    Write-Host 'Subindo Propostas (:5180), Subscricao (:5181), Cobranca (:5182), Notificacao (:5183), Operacao (:5184) e painel (:5173)...'
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
    Write-Host 'Tudo no ar: http://localhost:5173' -ForegroundColor Green
    if ($ComAspire) { Write-Host 'Traces ao vivo no Aspire Dashboard: http://localhost:18888' -ForegroundColor Green }
    Write-Host 'A primeira chamada ao Service Bus pode levar ~20 s (DefaultAzureCredential procurando o login do az).'
    Write-Host 'Ctrl+C encerra tudo. Ao terminar de estudar: .\infra\down.ps1 (a infra cobra ~US$ 0,013/h).'
    if (-not $SemNavegador) {
        Start-Process "http://localhost:$portaDoPainel"
    }

    # 6) Fica de olho: se um app cair, avisa (o painel mostra a coluna dele como fora do ar)
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
