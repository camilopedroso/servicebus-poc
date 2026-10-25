# POC Azure Service Bus — Emissão de apólice de seguro de vida

POC para estudar Azure Service Bus em C#/.NET 10, comparando com Kafka e RabbitMQ.

| Fase | Tema                                                                          | Status |
| ---- | ----------------------------------------------------------------------------- | ------ |
| 1    | Fila: peek-lock, Complete/Abandon/DeadLetter, retry, DLQ, competing consumers | ✅      |
| 2    | Tópico + assinaturas com filtros, transação entre entidades                   | ✅      |
| 3    | Sessions, detecção de duplicatas, mensagens agendadas, deferral               | ✅      |
| 4    | API REST + Outbox no SQL Server                                               | ✅      |
| 5    | Observabilidade (OpenTelemetry → App Insights) + testes                       | ✅      |
| 6    | (Opcional) Azure DevOps + Azure Functions                                     | ⏳      |

Notas de estudo: [fase 1 — filas](docs/fase-1-filas.md) · [fase 2 — tópicos](docs/fase-2-topicos.md) · [fase 3 — sessions](docs/fase-3-sessions.md) · [fase 4 — outbox](docs/fase-4-outbox.md) · [fase 5 — observabilidade e testes](docs/fase-5-observabilidade.md)

## Estrutura

```
infra/
  main.bicep         namespace Standard (só Entra ID, sem SAS), fila "propostas",
                     tópico "apolice-emitida" com 3 assinaturas e filtros, tópico "proposta-recusada",
                     fila "endossos" (sessions + detecção de duplicatas), Application Insights + Log Analytics
                     (só Entra ID, teto diário de 0,1 GB), RBAC
  up.ps1             cria/atualiza a infra e grava src/servicebus.local.json
  down.ps1           apaga tudo (para a cobrança)
  sql-up.ps1         SQL Server 2022 local em Docker (porta 14330, fase 4) e grava src/sqlserver.local.json
  sql-down.ps1       remove o container (-ApagarDados remove também o volume)
src/
  Vida.Mensageria/   contratos (PropostaSeguroVida, ApoliceEmitida) + criação do ServiceBusClient
  Vida.LogsAoVivo/   logger que guarda os últimos logs e os transmite em GET /logs (SSE) para o painel
  Vida.Telemetria/   OpenTelemetry nos três apps (Microsoft OpenTelemetry Distro → App Insights e/ou OTLP)
  Vida.Producer/     API (:5180): envia propostas/apólices e tem as operações (peek, DLQ, reprocessar, regras);
                     fase 4: API REST /api/propostas com outbox no SQL Server (Api/, Dados/, Outbox/)
  Vida.Consumer/     (:5181) lê a fila; ao aprovar, completa e publica no tópico numa transação;
                     Endossos/ aplica os endossos de cada apólice em ordem (sessions, session state, deferral)
  Vida.Assinantes/   (:5182) um processor por assinatura (cobranca, notificacao, alto-valor), liga/desliga por HTTP
tests/
  Vida.Testes.Unidade/     xUnit v3: domínio, contratos, mensagens, decisão de endosso, outbox
  Vida.Testes.Integracao/  API + outbox contra SQL Server real (Testcontainers; precisa de Docker)
web/
  painel/            React + Vite: escolhe e executa cenários, mostra os logs dos três apps lado a lado
```

## Pré-requisitos

- .NET 10 SDK
- Node.js 20+ (para o painel)
- Docker Desktop (SQL Server da fase 4; sem ele, as fases 1 a 3 funcionam e a API de propostas responde 503)
- Azure CLI logado (`az login`) — o código autentica com `DefaultAzureCredential`, sem connection string

## Painel

Um comando sobe tudo e abre o navegador (Ctrl+C encerra; a saída de cada app fica em `logs\`):

```powershell
.\iniciar.ps1               # compila, sobe Producer, Consumer, Assinantes e o painel
.\iniciar.ps1 -ComInfra     # antes, cria/atualiza a infra (infra\up.ps1)
.\iniciar.ps1 -ComInfra -ModoDebug   # idem, com lock de 5 min para depurar (ver "Modo debug")
.\iniciar.ps1 -Parar        # encerra processos que ficaram para trás (ex.: janela fechada no X)
# ou no prompt do Claude -> ! pwsh -File iniciar.ps1
```

Para depurar um app, suba-o pelo VS Code (configuração **Fluxo completo (Consumer + Assinantes + Producer)**) ou
cada um no seu terminal:

```powershell
dotnet run --project src\Vida.Producer
dotnet run --project src\Vida.Consumer
dotnet run --project src\Vida.Assinantes
cd web\painel; npm install; npm run dev     # http://localhost:5173
```

- **Cenário**: o dropdown lista os cenários das fases 1 a 4, cada um com seus parâmetros (idade, capital, plano,
  falha simulada, assinatura). Campo vazio = o Producer sorteia. Não é preciso reiniciar nada para trocar de cenário.
- **Colunas**: logs ao vivo do Producer, do Consumer e do Assinantes. Se um app cair, a coluna mostra o comando para
  subir de novo e reconecta sozinha.
- **Trilha**: clique em um id (proposta ou apólice) para destacar a mesma mensagem nas três colunas. Linhas sem
  id de negócio (ex.: "Lote enviado com N mensagens") entram pelo TraceId da fase 5.
- **Selos**: cada coluna é um processo; o selo diz de qual componente dele a linha veio (`API`, `outbox`, `cenário`
  no Producer; `propostas`, `endossos` no Consumer; a assinatura no Assinantes).
- **Trace** (fase 5): no fim de cada linha, abre o trace no Aspire Dashboard (com `-ComAspire`) ou copia o TraceId
  para buscar no Application Insights.
- **Assinaturas ligadas**: no cabeçalho da coluna do Assinantes, liga e desliga cada assinatura e mostra quantas
  mensagens estão pendentes.

Os endpoints também podem ser chamados direto (lista no início de `src/Vida.Producer/Program.cs`):

```powershell
curl -X POST localhost:5180/propostas -H "Content-Type: application/json" -d '{\"idade\":85}'
curl localhost:5180/assinaturas/regras
```

## Custo

O tier Standard cobra **~US$ 0,013/hora (~US$ 10/mês)** enquanto o namespace existir, mesmo parado.
As primeiras 13 milhões de operações/mês estão inclusas. Por isso:

```powershell
.\infra\up.ps1     # ao começar a estudar (~1 min)
# ou no prompt do Claude -> ! pwsh -File infra/up.ps1

.\infra\down.ps1   # ao terminar
# ou no prompt do Claude -> ! pwsh -File infra/down.ps1
```

### Modo debug

Parado num breakpoint, o SDK não renova o lock. Com o lock normal de 30 s, a mensagem volta para a fila e o
`Complete` falha com `MessageLockLost` ("The lock supplied is invalid"). Para depurar com calma:

```powershell
.\infra\up.ps1 -ModoDebug   # lock de 5 min (máximo do Service Bus) na fila e nas assinaturas
# ou no prompt do Claude -> ! pwsh -File infra/up.ps1 -ModoDebug

.\infra\up.ps1              # volta para 30 s
# ou no prompt do Claude -> ! pwsh -File infra/up.ps1
```

Mesmo com 5 min, se ficar mais tempo que isso parado, o erro volta. Em produção o lock fica curto de propósito:
se um consumer travar, a mensagem volta logo para outro processar.

## Rodando a fase 1

Com os apps e o painel no ar (seção **Painel**), use os cenários do grupo "Fase 1: fila propostas". A primeira chamada
pode levar ~20 s enquanto o `DefaultAzureCredential` acha o login do Azure CLI.

| Cenário no painel                  | O que acontece                                                     |
| ---------------------------------- | ------------------------------------------------------------------ |
| Enviar lote de propostas válidas   | N propostas em batch; o Consumer aprova cada uma                   |
| Enviar uma proposta sob medida     | idade, capital, plano e falha simulada escolhidos por você         |
| Proposta recusada por idade        | idade 85 → recusa publicada em `proposta-recusada`, cliente avisado |
| Falha transitória até a DLQ        | 5 entregas seguidas → DLQ (`MaxDeliveryCountExceeded`)             |
| Falha irrecuperável                | corpo ilegível → DLQ na 1ª entrega (`PayloadInvalido`)             |
| Espiar a fila / Espiar a DLQ       | peek, sem consumir; na DLQ mostra o motivo                         |
| Reprocessar a DLQ                  | devolve a DLQ para a fila (payload inválido fica)                  |

Experimentos sugeridos:

1. **Competing consumers**: suba um segundo consumer em outra porta (`dotnet run --project src\Vida.Consumer -- --urls http://localhost:5191`) e envie um lote de 20. Cada mensagem é processada por só um deles (o log mostra a instância). O painel mostra a instância da porta 5181.
2. **Lock expirando**: pare o consumer com a mensagem em processamento (ou aumente o `Task.Delay` para além do `MaxAutoLockRenewalDuration`) e veja a mensagem voltar com `DeliveryCount` maior.
3. **Portal**: em portal.azure.com → namespace `sb-vida-poc-*` → fila `propostas`, veja contagem de mensagens ativas/DLQ e use o **Service Bus Explorer**.

## Rodando a fase 2

Fluxo completo: proposta na fila → consumer aprova e publica `ApoliceEmitida` → cada assinatura recebe sua cópia.
No painel, o lote da fase 1 já percorre o fluxo inteiro. Para testar só os filtros, sem a fila, use o grupo
"Fase 2: tópico apolice-emitida":

| Cenário no painel                  | O que acontece                                                     |
| ---------------------------------- | ------------------------------------------------------------------ |
| Publicar apólice direto no tópico  | capital até 1 milhão → cobranca + notificacao                      |
| Publicar apólice de alto valor     | 1,5 milhão → notificacao + alto-valor (prioridade alta), sem cobrança |
| Espiar uma assinatura / sua DLQ    | peek na assinatura escolhida                                       |
| Ler as regras das assinaturas      | filtros e actions aplicados pelo broker                            |

Experimentos sugeridos:

1. **Assinante parado não perde nada**: no seletor "Assinaturas ligadas" da coluna do Assinantes, desligue `notificacao`, publique apólices e veja as pendentes subirem. Religue e ela processa o acúmulo.
2. **Competing consumers numa assinatura**: suba uma segunda instância só com cobrança (`dotnet run --project src\Vida.Assinantes -- cobranca --urls=http://localhost:5192`) e veja as apólices divididas entre as duas, enquanto `notificacao` continua recebendo todas.
3. **Nova assinatura**: crie uma no portal com filtro SQL `plano = 'Empresarial'`. Ela só recebe o que for publicado depois de criada.

## Rodando a fase 3

Grupo "Fase 3: sessions, duplicatas e agendamento". Os endossos vão para a fila `endossos` e aparecem na coluna do
Consumer; clique no id de uma apólice para seguir só os endossos dela.

| Cenário no painel                           | O que acontece                                                          |
| ------------------------------------------- | ----------------------------------------------------------------------- |
| Endossos em ordem (sessions)                | 3 apólices × 4 versões intercaladas; cada apólice aplica v1→v4 em ordem |
| Endossos fora de ordem (deferral)           | 5 versões embaralhadas; adiantadas são adiadas e retomadas em sequência |
| Endosso duplicado (detecção de duplicatas)  | v1 enviada duas vezes com o mesmo MessageId; o broker descarta a cópia  |
| Falha transitória com backoff (agendamento) | tentativas agendadas a 2 s e 4 s; aprovada na 3ª                        |

Experimentos sugeridos:

1. **Sessões entre instâncias**: suba um segundo consumer (`dotnet run --project src\Vida.Consumer -- --urls http://localhost:5191`) e envie endossos em ordem com várias apólices. Cada apólice fica inteira numa instância de cada vez; as apólices se dividem entre as duas.
2. **Duplicata fora da janela**: a janela é de 10 min. Um reenvio depois disso passa pelo broker e é barrado pelo consumer (`versao <= UltimaVersao`): a idempotência no consumer continua necessária.
3. **Versão que nunca chega**: com o Producer parado no meio de uma sequência, as versões posteriores ficam adiadas. Elas aparecem no peek do portal (Service Bus Explorer) com estado *Deferred*.

## Rodando a fase 4

O `iniciar.ps1` sobe o SQL Server em Docker antes dos apps (`infra\sql-up.ps1`, que também pode ser rodado sozinho).
Na primeira partida, o Producer cria o banco `VidaPropostas` pelas migrations do EF Core. Grupo "Fase 4: API REST +
outbox (SQL Server)":

| Cenário no painel                        | O que acontece                                                             |
| ---------------------------------------- | -------------------------------------------------------------------------- |
| Criar proposta pela API REST (outbox)    | 201 + Location; o despachante publica em ~1 s; GET mostra EnviadaParaAnalise |
| POST repetido com Idempotency-Key        | 201 e depois 200 com `Idempotent-Replayed: true`, mesmo id                 |
| Service Bus fora do ar (outbox acumula)  | a API segue respondendo 201; o outbox acumula com backoff                  |
| Restabelecer o Service Bus               | as pendentes saem na ordem de gravação                                     |
| Sem outbox: dual write com falha         | a proposta é gravada e nunca publicada (fica em Recebida)                  |
| Ver o outbox                             | pendentes e publicadas                                                     |

A API também pode ser chamada direto. O contrato fica em `http://localhost:5180/openapi/v1.json`:

```powershell
curl -i -X POST localhost:5180/api/propostas -H "Content-Type: application/json" -H "Idempotency-Key: abc-123" `
  -d '{\"clienteId\":\"CLI-1\",\"idade\":40,\"capitalSegurado\":300000,\"plano\":\"Familiar\"}'
```

Para olhar as tabelas: conecte em `localhost,14330` (usuário `sa`, senha em `src\sqlserver.local.json`) pelo
SSMS, Azure Data Studio ou a extensão SQL Server do VS Code.

## Rodando a fase 5

**Telemetria.** Depois do `up.ps1`, os três apps mandam traces, métricas e logs para o Application Insights
(autenticação Entra ID, sem chave). Os dados aparecem no portal em 2 a 5 minutos: Application Map, Transaction search
(busque pelo id da proposta) ou Logs (KQL, exemplos em [docs/fase-5-observabilidade.md](docs/fase-5-observabilidade.md)).
Para ver na hora, suba com o Aspire Dashboard local:

```powershell
.\iniciar.ps1 -ComAspire    # traces, logs e métricas em http://localhost:18888 (além do App Insights)
```

Um POST em `/api/propostas` (cenário "Criar proposta pela API REST") gera um trace só, do HTTP até as assinaturas.

**Testes.** Com o Docker rodando (os de integração sobem um SQL Server descartável):

```powershell
dotnet test                                          # tudo (unidade + integração)
dotnet test --project tests\Vida.Testes.Unidade      # só unidade, sem infraestrutura
```