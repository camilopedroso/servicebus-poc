# POC Azure Service Bus — Emissão de apólice de seguro de vida

POC para estudar Azure Service Bus em C#/.NET 10, comparando com Kafka e RabbitMQ.

| Fase | Tema | Status |
|---|---|---|
| 1 | Fila: peek-lock, Complete/Abandon/DeadLetter, retry, DLQ, competing consumers | ✅ |
| 2 | Tópico + assinaturas com filtros | ⏳ |
| 3 | Sessions, detecção de duplicatas, mensagens agendadas, deferral | ⏳ |
| 4 | API REST + Outbox no SQL Server | ⏳ |
| 5 | Observabilidade (OpenTelemetry → App Insights) + testes | ⏳ |
| 6 | (Opcional) Azure DevOps + Azure Functions | ⏳ |

Notas de estudo: [docs/fase-1-filas.md](docs/fase-1-filas.md)

## Estrutura

```
infra/
  main.bicep         namespace Standard (só Entra ID, sem SAS) + fila "propostas" + RBAC
  up.ps1             cria a infra e grava src/servicebus.local.json
  down.ps1           apaga tudo (para a cobrança)
src/
  Vida.Mensageria/   contrato (PropostaSeguroVida) + criação do ServiceBusClient
  Vida.Producer/     envia propostas e tem comandos de operação (peek, DLQ, reprocessar)
  Vida.Consumer/     Worker Service com ServiceBusProcessor
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
