# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Purpose

Study POC for Azure Service Bus in C#/.NET 10, modeled on a life-insurance policy issuance domain ("Vida"). The goal is to explain Service Bus concepts by contrasting them with Kafka and RabbitMQ, which the user already knows. Code identifiers, comments, logs and docs are in **Portuguese (pt-BR)**. Keep that convention.

The work is split into phases (roadmap and status in `README.md`). Each phase gets code plus a study note in `docs/fase-N-*.md` with Kafka/RabbitMQ comparisons, and Microsoft Learn links. Done: 1 queues, settlement, DLQ; 2 topic + subscriptions with filters, cross-entity transaction. Next: 3 sessions/dedup/scheduled/deferral, 4 REST API + Outbox on SQL Server, 5 OpenTelemetry → App Insights + tests, 6 Azure DevOps + Functions (optional).

## Commands

```powershell
.\infra\up.ps1                 # create/update Azure infra (RG rg-servicebus-poc, brazilsouth) and write src/servicebus.local.json
.\infra\up.ps1 -ModoDebug      # same, with lockDuration PT5M (max) on queue and subscriptions, for breakpoint debugging
.\infra\down.ps1               # delete the resource group. Standard tier bills ~US$0.013/h while it exists, even idle
az bicep lint -f infra\main.bicep
dotnet build                   # solution: ServiceBusPoc.slnx
dotnet run --project src\Vida.Consumer
dotnet run --project src\Vida.Assinantes [-- cobranca notificacao alto-valor]   # default: all subscriptions
dotnet run --project src\Vida.Producer -- <command>   # full command list in the header comment of src/Vida.Producer/Program.cs
dotnet run --project src\Vida.Producer -- regras      # verify subscription rules after any Bicep change
```

There are no tests yet (planned for phase 5).

## Architecture

- **Config flow:** `infra/up.ps1` deploys `main.bicep` and writes the namespace FQDN to `src/servicebus.local.json` (gitignored). Both apps link that file into their output via a csproj `<None ... Condition="Exists(...)">` item and load it from `AppContext.BaseDirectory`. If the build ran before `up.ps1`, the file isn't copied, so rebuild after running it.
- **Auth is Entra ID only:** the namespace has `disableLocalAuth: true`, so SAS keys and connection strings will not work. `ServiceBusClientFactory` (in `Vida.Mensageria/Conexao/`) builds the single `ServiceBusClient` with `DefaultAzureCredential` (locally that's `az login`) and exponential retry. Bicep grants the deploying user `Azure Service Bus Data Owner`. RBAC can take minutes to propagate, so 401s right after `up.ps1` are expected. The first consumer connection takes ~20 s while `DefaultAzureCredential` walks its credential chain.
- **`Vida.Mensageria`:** organized by role, one class per file, namespace = `Vida.Mensageria.<Pasta>`:
  - `Contratos/`: message payloads (`PropostaSeguroVida`, `ApoliceEmitida`)
  - `Topologia/`: broker entity names (`Filas`, `Topicos`, `Assinaturas`). New queues, topics and subscriptions must be added both here and in `infra/main.bicep`.
  - `Mensagens/`: how messages are built (`MensagensApolice`, application-property names in `Propriedades`, `TiposDeMensagem`)
  - `Conexao/`: `ServiceBusClientFactory`
  - `Dominio/`: business rules that aren't messaging (`CalculadoraDePremio`)
- The user prefers one class per file and folders named after what the code does. Follow that for new code.
- **`Vida.Consumer` (`Worker.cs`):** a `BackgroundService` around `ServiceBusProcessor` in PeekLock mode with `AutoCompleteMessages = false`. Settlement is explicit and demonstrates the distinct DLQ paths:
  - unreadable body → `DeadLetter("PayloadInvalido")`
  - application property `simularFalha=true` → `Abandon`. The broker dead-letters it after the queue's `maxDeliveryCount` (5) with reason `MaxDeliveryCountExceeded`.
  - age > 80 → `DeadLetter("RegraDeNegocio")`
  - otherwise → `Complete`
  - Note that `Abandon` redelivers immediately with no backoff. This is an intended teaching point.
- **Phase 2 flow:** on the happy path, `Vida.Consumer` completes the queue message and sends `ApoliceEmitida` to topic `apolice-emitida` inside one `TransactionScope`. This needs `EnableCrossEntityTransactions` on its client (`ServiceBusClientFactory.Criar(..., transacoesEntreEntidades: true)`), the sender created from that same client, and Complete as the first operation so the queue is the "via" entity. `Vida.Assinantes` registers one `AssinaturaWorker` hosted service per subscription, all sharing one client.
- **Subscription filters only see properties, never the body.** `MensagensApolice.Criar` copies routing fields (`plano`, `capitalSegurado` as `long`) into application properties. Change those together with the Bicep rules. In Bicep, don't redefine the `$Default` rule: the deploy succeeds but leaves the subscription with no rules. Give each rule its own name.
- **`Vida.Producer`:** a console app with `switch`-dispatched commands. It sets `MessageId` to the business id (groundwork for duplicate detection in phase 3), `Subject` to the event type (for subscription filters in phase 2), and `CorrelationId` to the client id. It also hosts the operations tools: peek, DLQ peek, and DLQ resubmit. Resubmit sends the copy before completing the DLQ message, so a failure duplicates rather than loses (at-least-once).

## Working with the user

- Talk to the user in Portuguese. They read the code closely, ask concept questions as they go, and add XML doc comments themselves. Keep their edits.
- Commit only when asked, directly on `master`, with no branches. Before committing, check for and mention the user's own uncommitted edits.
- Infra costs money while it exists. At the end of a session, or after a deploy the user didn't ask to keep, remind them to run `down.ps1`. Check with `az group exists -n rg-servicebus-poc`.
- The `!` prompt prefix runs **bash**, which eats backslashes. Suggest `! pwsh -File infra/up.ps1` (or `down.ps1`, `-ModoDebug`), not `.\infra\up.ps1`.
- Before writing Service Bus facts into docs (limits, semantics), verify them with the Azure MCP `documentation` tool. One claim about multiple matching rules was wrong until checked.

## Conventions for new phases

- Follow the Azure best-practice choices already made: managed identity/RBAC instead of keys, one `ServiceBusClient` singleton per app, infrastructure in Bicep rather than portal clicks, latest stable SDKs (`Azure.Messaging.ServiceBus`, `Azure.Identity`).
- Keep cost in mind: stay on the Standard tier (Premium is ~US$0.93/h). For anything stateful that isn't Service Bus, such as SQL Server for the outbox, run it locally in Docker.
- When finishing a phase, update the status table in `README.md` and add `docs/fase-N-*.md` in the same style as `docs/fase-1-filas.md`.
