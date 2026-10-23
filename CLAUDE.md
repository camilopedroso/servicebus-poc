# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Purpose

Study POC for Azure Service Bus in C#/.NET 10, modeled on a life-insurance policy issuance domain ("Vida"). The goal is to explain Service Bus concepts by contrasting them with Kafka and RabbitMQ, which the user already knows. Code identifiers, comments, logs and docs are in **Portuguese (pt-BR)**. Keep that convention.

The work is split into phases (roadmap and status in `README.md`). Each phase gets code plus a study note in `docs/fase-N-*.md` with Kafka/RabbitMQ comparisons, and Microsoft Learn links. Phase 1 (queues, settlement, DLQ) is done. Next: 2 topics + subscriptions with filters, 3 sessions/dedup/scheduled/deferral, 4 REST API + Outbox on SQL Server, 5 OpenTelemetry → App Insights + tests, 6 Azure DevOps + Functions (optional).

## Commands

```powershell
.\infra\up.ps1                 # create/update Azure infra (RG rg-servicebus-poc, brazilsouth) and write src/servicebus.local.json
.\infra\down.ps1               # delete the resource group. Standard tier bills ~US$0.013/h while it exists, even idle
az bicep lint -f infra\main.bicep
dotnet build                   # solution: ServiceBusPoc.slnx
dotnet run --project src\Vida.Consumer
dotnet run --project src\Vida.Producer -- <enviar [n] | enviar-invalida | enviar-com-falha | espiar | dlq | reprocessar-dlq>
```

There are no tests yet (planned for phase 5).

## Architecture

- **Config flow:** `infra/up.ps1` deploys `main.bicep` and writes the namespace FQDN to `src/servicebus.local.json` (gitignored). Both apps link that file into their output via a csproj `<None ... Condition="Exists(...)">` item and load it from `AppContext.BaseDirectory`. If the build ran before `up.ps1`, the file isn't copied, so rebuild after running it.
- **Auth is Entra ID only:** the namespace has `disableLocalAuth: true`, so SAS keys and connection strings will not work. `Vida.Mensageria/Mensageria.cs` (`ServiceBusClientFactory`) builds the single `ServiceBusClient` with `DefaultAzureCredential` (locally that's `az login`) and exponential retry. Bicep grants the deploying user `Azure Service Bus Data Owner`. RBAC can take minutes to propagate, so 401s right after `up.ps1` are expected. The first consumer connection takes ~20 s while `DefaultAzureCredential` walks its credential chain.
- **`Vida.Mensageria`:** shared contract (`PropostaSeguroVida`), entity names (`Filas`), application-property names (`Propriedades`). New queues, topics and subscriptions must be added in both `infra/main.bicep` and these constants.
- **`Vida.Consumer` (`Worker.cs`):** a `BackgroundService` around `ServiceBusProcessor` in PeekLock mode with `AutoCompleteMessages = false`. Settlement is explicit and demonstrates the distinct DLQ paths:
  - unreadable body → `DeadLetter("PayloadInvalido")`
  - application property `simularFalha=true` → `Abandon`. The broker dead-letters it after the queue's `maxDeliveryCount` (5) with reason `MaxDeliveryCountExceeded`.
  - age > 80 → `DeadLetter("RegraDeNegocio")`
  - otherwise → `Complete`
  - Note that `Abandon` redelivers immediately with no backoff. This is an intended teaching point.
- **`Vida.Producer`:** a console app with `switch`-dispatched commands. It sets `MessageId` to the business id (groundwork for duplicate detection in phase 3), `Subject` to the event type (for subscription filters in phase 2), and `CorrelationId` to the client id. It also hosts the operations tools: peek, DLQ peek, and DLQ resubmit. Resubmit sends the copy before completing the DLQ message, so a failure duplicates rather than loses (at-least-once).

## Conventions for new phases

- Follow the Azure best-practice choices already made: managed identity/RBAC instead of keys, one `ServiceBusClient` singleton per app, infrastructure in Bicep rather than portal clicks, latest stable SDKs (`Azure.Messaging.ServiceBus`, `Azure.Identity`).
- Keep cost in mind: stay on the Standard tier (Premium is ~US$0.93/h). For anything stateful that isn't Service Bus, such as SQL Server for the outbox, run it locally in Docker.
- When finishing a phase, update the status table in `README.md` and add `docs/fase-N-*.md` in the same style as `docs/fase-1-filas.md`.
