# POC Azure Service Bus — Emissão de apólice de seguro de vida

POC para estudar Azure Service Bus em C#/.NET 10, comparando com Kafka e RabbitMQ.

| Fase | Tema                                                                          | Status |
| ---- | ----------------------------------------------------------------------------- | ------ |
| 1    | Fila: peek-lock, Complete/Abandon/DeadLetter, retry, DLQ, competing consumers | ✅      |
| 2    | Tópico + assinaturas com filtros, transação entre entidades                   | ✅      |
| 3    | Sessions, detecção de duplicatas, mensagens agendadas, deferral               | ⏳      |
| 4    | API REST + Outbox no SQL Server                                               | ⏳      |
| 5    | Observabilidade (OpenTelemetry → App Insights) + testes                       | ⏳      |
| 6    | (Opcional) Azure DevOps + Azure Functions                                     | ⏳      |

Notas de estudo: [fase 1 — filas](docs/fase-1-filas.md) · [fase 2 — tópicos](docs/fase-2-topicos.md)

## Estrutura

```
infra/
  main.bicep         namespace Standard (só Entra ID, sem SAS), fila "propostas",
                     tópico "apolice-emitida" com 3 assinaturas e filtros, tópico "proposta-recusada", RBAC
  up.ps1             cria/atualiza a infra e grava src/servicebus.local.json
  down.ps1           apaga tudo (para a cobrança)
src/
  Vida.Mensageria/   contratos (PropostaSeguroVida, ApoliceEmitida) + criação do ServiceBusClient
  Vida.LogsAoVivo/   logger que guarda os últimos logs e os transmite em GET /logs (SSE) para o painel
  Vida.Producer/     API (:5180): envia propostas/apólices e tem as operações (peek, DLQ, reprocessar, regras)
  Vida.Consumer/     (:5181) lê a fila; ao aprovar, completa e publica no tópico numa transação
  Vida.Assinantes/   (:5182) um processor por assinatura (cobranca, notificacao, alto-valor), liga/desliga por HTTP
web/
  painel/            React + Vite: escolhe e executa cenários, mostra os logs dos três apps lado a lado
```

## Pré-requisitos

- .NET 10 SDK
- Node.js 20+ (para o painel)
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

- **Cenário**: o dropdown lista os cenários das fases 1 e 2, cada um com seus parâmetros (idade, capital, plano,
  falha simulada, assinatura). Campo vazio = o Producer sorteia. Não é preciso reiniciar nada para trocar de cenário.
- **Colunas**: logs ao vivo do Producer, do Consumer e do Assinantes. Se um app cair, a coluna mostra o comando para
  subir de novo e reconecta sozinha.
- **Trilha**: clique em um id (proposta ou apólice) para destacar a mesma mensagem nas três colunas.
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
| Publicar apólice de alto valor     | 1,5 milhão → cobranca + notificacao + alto-valor (prioridade alta) |
| Espiar uma assinatura / sua DLQ    | peek na assinatura escolhida                                       |
| Ler as regras das assinaturas      | filtros e actions aplicados pelo broker                            |

Experimentos sugeridos:

1. **Assinante parado não perde nada**: no seletor "Assinaturas ligadas" da coluna do Assinantes, desligue `notificacao`, publique apólices e veja as pendentes subirem. Religue e ela processa o acúmulo.
2. **Competing consumers numa assinatura**: suba uma segunda instância só com cobrança (`dotnet run --project src\Vida.Assinantes -- cobranca --urls=http://localhost:5192`) e veja as apólices divididas entre as duas, enquanto `notificacao` continua recebendo todas.
3. **Nova assinatura**: crie uma no portal com filtro SQL `plano = 'Empresarial'`. Ela só recebe o que for publicado depois de criada.
