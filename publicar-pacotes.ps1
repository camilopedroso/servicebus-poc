# Publica no feed local (pacotes/) o que as squads compartilham: a plataforma e os contratos.
# Entre squads o código chega como pacote, com versão; dentro da squad, ProjectReference (ver docs/como-funciona.md, seção 6).
# Uso: .\publicar-pacotes.ps1            publica o que ainda não está no feed (idempotente; o iniciar.ps1 chama sempre)
#      .\publicar-pacotes.ps1 -Forcar    republica a MESMA versão (só para desenvolvimento: ver o comentário de -Forcar)
# Mudou um contrato ou a plataforma? Suba a <Version> no csproj e rode de novo: quem consome atualiza quando quiser.
param(
    [switch]$Forcar
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$feed = Join-Path $raiz 'pacotes'
New-Item -ItemType Directory -Force $feed | Out-Null

# Ordem de dependência: Vida.Subscricao.Contratos depende do pacote Vida.Propostas.Contratos (o Plano), então vem depois.
$projetos = @(
    'src\Plataforma\Vida.Plataforma\Vida.Plataforma.csproj',
    'src\Plataforma\Vida.Telemetria\Vida.Telemetria.csproj',
    'src\Propostas\Vida.Propostas.Contratos\Vida.Propostas.Contratos.csproj',
    'src\Subscricao\Vida.Subscricao.Contratos\Vida.Subscricao.Contratos.csproj'
)

# Cache global do NuGet (~/.nuget/packages): o restore guarda cada pacote pela versão e nunca mais baixa a mesma.
$cacheGlobal = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''

foreach ($relativo in $projetos) {
    $projeto = Join-Path $raiz $relativo
    $id = [IO.Path]::GetFileNameWithoutExtension($projeto)
    $versao = dotnet msbuild $projeto -getProperty:Version -nologo
    $pacote = Join-Path $feed "$id.$versao.nupkg"

    if ((Test-Path $pacote) -and -not $Forcar) {
        Write-Host "$id $versao já está no feed. Para publicar uma mudança, suba a <Version> no csproj."
        continue
    }
    if ($Forcar) {
        # Versão publicada deveria ser imutável: quem restaurou a 1.0.0 continuaria com a cópia antiga do cache.
        # -Forcar apaga a cópia do feed e do cache desta máquina, o que só resolve aqui. Em equipe, suba a versão.
        Remove-Item $pacote -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $cacheGlobal "$($id.ToLowerInvariant())\$versao") -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Publicando $id $versao..."
    dotnet pack $projeto -c Release -o $feed --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Falhou o pack de $id." }
}
