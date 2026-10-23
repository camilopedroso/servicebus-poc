# POC Azure Service Bus — Emissão de apólice de seguro de vida

POC para estudar Azure Service Bus em C#/.NET 10, comparando com Kafka e RabbitMQ.

| Fase | Tema | Status |
|---|---|---|
| 1 | Fila: peek-lock, Complete/Abandon/DeadLetter, retry, DLQ, competing consumers | ✅ |
| 2 | Tópico + assinaturas com filtros, transação entre entidades | ✅ |
| 3 | Sessions, detecção de duplicatas, mensagens agendadas, deferral | ⏳ |
| 4 | API REST + Outbox no SQL Server | ⏳ |
| 5 | Observabilidade (OpenTelemetry → App Insights) + testes | ⏳ |
| 6 | (Opcional) Azure DevOps + Azure Functions | ⏳ |

Notas de estudo: [fase 1 — filas](docs/fase-1-filas.md) · [fase 2 — tópicos](docs/fase-2-topicos.md)

## Estrutura

```
infra/
  main.bicep         namespace Standard (só Entra ID, sem SAS), fila "propostas",
                     tópico "apolice-emitida" com 3 assinaturas e filtros, RBAC
  up.ps1             cria/atualiza a infra e grava src/servicebus.local.json
  down.ps1           apaga tudo (para a cobrança)
src/
  Vida.Mensageria/   contratos (PropostaSeguroVida, ApoliceEmitida) + criação do ServiceBusClient
  Vida.Producer/     envia propostas/apólices e tem comandos de operação (peek, DLQ, reprocessar, regras)
  Vida.Consumer/     lê a fila; ao aprovar, completa e publica no tópico numa transação
  Vida.Assinantes/   um processor por assinatura (cobranca, notificacao, alto-valor)
```

## Pré-requisitos

- .NET 10 SDK
- Azure CLI logado (`az login`) — o código autentica com `DefaultAzureCredential`, sem connection string

## Custo

O tier Standard cobra **~US$ 0,013/hora (~US$ 10/mês)** enquanto o namespace existir, mesmo parado.
As primeiras 13 milhões de operações/mês estão inclusas. Por isso:

```powershell
.\infra\up.ps1     # ao começar a estudar (~1 min)
.\infra\down.ps1   # ao terminar
```

## Rodando a fase 1

Terminal 1 — consumer (a primeira conexão pode levar ~20 s enquanto o `DefaultAzureCredential` acha o login do Azure CLI):

```powershell
dotnet run --project src\Vida.Consumer
```

Terminal 2 — producer:

```powershell
dotnet run --project src\Vida.Producer -- enviar 10          # 10 propostas válidas, em lote
dotnet run --project src\Vida.Producer -- enviar-invalida    # idade 85 → DLQ (RegraDeNegocio)
dotnet run --project src\Vida.Producer -- enviar-com-falha   # 5 tentativas → DLQ (MaxDeliveryCountExceeded)
dotnet run --project src\Vida.Producer -- espiar             # peek na fila
dotnet run --project src\Vida.Producer -- dlq                # peek na DLQ com o motivo
dotnet run --project src\Vida.Producer -- reprocessar-dlq    # devolve a DLQ para a fila
```

Experimentos sugeridos:

1. **Competing consumers**: abra 2 consumers e envie `enviar 20`. Cada mensagem aparece em só um deles (o log mostra a instância).
2. **Lock expirando**: pare o consumer com a mensagem em processamento (ou aumente o `Task.Delay` para além do `MaxAutoLockRenewalDuration`) e veja a mensagem voltar com `DeliveryCount` maior.
3. **Portal**: em portal.azure.com → namespace `sb-vida-poc-*` → fila `propostas`, veja contagem de mensagens ativas/DLQ e use o **Service Bus Explorer**.

## Rodando a fase 2

Fluxo completo: proposta na fila → consumer aprova e publica `ApoliceEmitida` → cada assinatura recebe sua cópia.
No VS Code, use a configuração **Fluxo completo (Consumer + Assinantes + Producer)**. Pelo terminal:

```powershell
dotnet run --project src\Vida.Consumer                       # terminal 1
dotnet run --project src\Vida.Assinantes                     # terminal 2 (todas as assinaturas)
dotnet run --project src\Vida.Assinantes -- alto-valor       # ...ou só algumas
dotnet run --project src\Vida.Producer -- enviar 5           # terminal 3
```

Para testar só os filtros, sem a fila:

```powershell
dotnet run --project src\Vida.Producer -- publicar-apolice 500000     # cobranca + notificacao
dotnet run --project src\Vida.Producer -- publicar-apolice 1500000    # cobranca + notificacao + alto-valor
dotnet run --project src\Vida.Producer -- espiar-assinatura alto-valor
dotnet run --project src\Vida.Producer -- dlq-assinatura cobranca
dotnet run --project src\Vida.Producer -- regras                      # filtros e actions aplicados pelo broker
```

Experimentos sugeridos:

1. **Assinante parado não perde nada**: rode `Vida.Assinantes -- cobranca`, envie propostas e depois suba `notificacao`. As mensagens estavam guardadas na assinatura dela.
2. **Competing consumers numa assinatura**: rode duas instâncias de `Vida.Assinantes -- cobranca` e veja as apólices divididas entre elas, enquanto `notificacao` continua recebendo todas.
3. **Nova assinatura**: crie uma no portal com filtro SQL `plano = 'Empresarial'`. Ela só recebe o que for publicado depois de criada.
