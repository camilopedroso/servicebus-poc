# Sobe tudo para estudar: Producer, Consumer, Assinantes e o painel, e abre o navegador.
# Uso: .\iniciar.ps1                  compila, sobe os 4 processos e abre http://localhost:5173
#      .\iniciar.ps1 -ComInfra        antes, cria/atualiza a infra no Azure (infra\up.ps1)
#      .\iniciar.ps1 -ComInfra -ModoDebug   idem, com lock de 5 min (máximo) para parar em breakpoints sem perder a mensagem
#      .\iniciar.ps1 -SemNavegador    não abre o navegador
#      .\iniciar.ps1 -Parar           encerra processos que ficaram para trás (ex.: janela fechada no X)
# Ctrl+C encerra tudo. A saída de cada app fica em logs\<app>.log; o painel mostra os mesmos logs ao vivo.
# No prompt do Claude: ! pwsh -File iniciar.ps1
param(
    [switch]$ComInfra,
    [switch]$ModoDebug,
    [switch]$SemNavegador,
    [switch]$Parar
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$apps = @(
    @{ Nome = 'Producer'; Porta = 5180 },
    @{ Nome = 'Consumer'; Porta = 5181 },
    @{ Nome = 'Assinantes'; Porta = 5182 }
)
$portaDoPainel = 5173
$todasAsPortas = @($apps.Porta) + $portaDoPainel

function Parar-Tudo {
    # Pelo nome do executável: matar só o "dotnet run" deixaria o app filho vivo segurando a porta.
    Get-Process -Name ($apps | ForEach-Object { "Vida.$($_.Nome)" }) -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Get-NetTCPConnection -LocalPort $portaDoPainel -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
}

function Porta-Em-Uso([int]$porta) {
    [bool](Get-NetTCPConnection -LocalPort $porta -State Listen -ErrorAction SilentlyContinue)
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

# 2) Portas livres
$ocupadas = $todasAsPortas | Where-Object { Porta-Em-Uso $_ }
if ($ocupadas) {
    throw "Porta(s) em uso: $($ocupadas -join ', '). Já está rodando? Use .\iniciar.ps1 -Parar e tente de novo."
}

# 3) Build (uma vez para a solução; os apps sobem a partir dos binários)
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
try {
    foreach ($app in $apps) {
        # Sobe o executável direto (não "dotnet run"): assim o PID guardado é o do app, e o Ctrl+C o encerra de fato.
        $pastaDoBinario = "$raiz\src\Vida.$($app.Nome)\bin\Debug\net10.0"
        $processos += Start-Process -FilePath "$pastaDoBinario\Vida.$($app.Nome).exe" `
            -WorkingDirectory $pastaDoBinario -NoNewWindow -PassThru `
            -RedirectStandardOutput "$pastaDeLogs\$($app.Nome).log" -RedirectStandardError "$pastaDeLogs\$($app.Nome).erro.log"
    }
    $processos += Start-Process -FilePath 'node' -ArgumentList 'node_modules/vite/bin/vite.js', '--port', $portaDoPainel, '--strictPort' `
        -WorkingDirectory $painel -NoNewWindow -PassThru `
        -RedirectStandardOutput "$pastaDeLogs\Painel.log" -RedirectStandardError "$pastaDeLogs\Painel.erro.log"

    # 5) Espera todos responderem
    Write-Host 'Subindo Producer (:5180), Consumer (:5181), Assinantes (:5182) e painel (:5173)...'
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
