# Fase 2 — Tópicos, assinaturas e filtros

## O fluxo da POC

```
fila "propostas" ──► Vida.Consumer (subscrição) ──[transação: Complete + Send]──► tópico "apolice-emitida"
                                                                                   │
                         ┌───────────────────────────────┬──────────────────────────┴──────────────┐
                 assinatura "cobranca"          assinatura "notificacao"            assinatura "alto-valor"
                 SQL: sys.Label =               (sem filtro: $Default =             SQL: requerRevisao = TRUE
                 'ApoliceEmitida' AND           TrueFilter)                         action: SET prioridade = 'alta'
                 requerRevisao = FALSE
                         │                               │                                     │
                         └──────────────── Vida.Assinantes (1 processor por assinatura) ──────┘
```

## Fila × tópico

| | Fila | Tópico + assinaturas |
|---|---|---|
| Destinatários | Um destinatário lógico (vários consumers **dividem** as mensagens) | Cada assinatura recebe **sua cópia**; dentro de cada assinatura os consumers dividem |
| Semântica típica | **Comando**: "analise esta proposta" | **Evento**: "a apólice foi emitida" |
| Acoplamento | Producer sabe quem processa | Publisher não sabe quem assina; dá para incluir assinantes novos sem mexer nele |
| Na POC | `propostas` | `apolice-emitida` |

Uma assinatura se comporta como uma fila: tem `lockDuration`, `maxDeliveryCount`, TTL e **DLQ próprios**.
O endereço da DLQ é `apolice-emitida/subscriptions/cobranca/$DeadLetterQueue`.

## Comparação

| Service Bus | RabbitMQ | Kafka |
|---|---|---|
| Tópico | Exchange (topic/headers) | Tópico |
| Assinatura | Fila ligada ao exchange | Consumer group |
| Regra (filtro) da assinatura | Binding (routing key / headers) | Não tem: o consumer lê tudo e descarta |
| Action da regra | Não tem | Não tem (seria um stream processor) |
| A mensagem é **copiada** para cada assinatura que aceita | Igual: cada fila tem sua cópia | Não copia: cada grupo tem seu offset no mesmo log |
| Assinatura nova só recebe o que chegar **depois** de criada | Igual | Pode ler desde o início (`earliest`), se a retenção permitir |

## Filtros

| Tipo | Como funciona | Quando usar |
|---|---|---|
| **TrueFilter** (`1=1`) | Aceita tudo. É a regra `$Default` de uma assinatura criada sem regras | Assinante que quer todos os eventos (`notificacao`) |
| **Correlation filter** | Igualdade em `Subject`, `CorrelationId`, `MessageId`, `To`, `ReplyTo`, `ContentType`, `SessionId` e/ou application properties. Vários campos = AND | A maioria dos casos (rota por tipo de evento, por tenant, por produto). Avaliado por hash: **mais barato** |
| **SQL filter** | Expressão SQL-92 sobre as propriedades: `>`, `<`, `LIKE`, `IN`, `AND`, `OR`, `EXISTS`... | Comparações por intervalo, combinações complexas (`alto-valor`). Mais caro: muitos SQL filters reduzem a vazão do tópico |
| **FalseFilter** (`1=0`) | Não aceita nada | Pausar uma assinatura sem apagá-la |

Regras importantes:

- **Filtros só enxergam propriedades, nunca o corpo.** Tudo que for usado para rotear precisa ir em
  `ApplicationProperties` ou nas propriedades de sistema. Na POC, `MensagensApolice.Criar` copia `plano` e
  `capitalSegurado` do corpo para as propriedades.
- Se a assinatura tem **várias regras sem action, vale o OR** e entra **uma cópia só**. Mas **cada regra com action
  que aceitar gera uma cópia a mais** (marcada com `RuleName`). Ex.: 3 regras sem action + 2 com action, todas
  aceitando → 3 mensagens na assinatura. Desenhe regras com action para não se sobreporem.
- **Action** (`SET`, `REMOVE`) altera só a cópia daquela assinatura. Na POC, só `alto-valor` vê `prioridade=alta`, e o
  broker ainda adiciona a propriedade `RuleName` com o nome da regra que aceitou.
- Mensagem que não passa em nenhuma assinatura **é descartada silenciosamente**. Se quiser detectar isso, crie uma
  assinatura "catch-all" de auditoria ou configure a regra SQL com `NOT (...)` das demais.
- `deadLetteringOnFilterEvaluationExceptions: true` manda para a DLQ a mensagem cujo filtro deu erro (ex.: tipo
  incompatível), em vez de perdê-la.

## Transação entre entidades (Complete na fila + Send no tópico)

O consumer precisa **completar a proposta e publicar a apólice** como uma coisa só:

| Ordem sem transação | Se cair no meio |
|---|---|
| Send → Complete | A proposta volta para a fila e é processada de novo → **evento duplicado** |
| Complete → Send | A proposta sumiu e o evento nunca saiu → **evento perdido** |

Com `EnableCrossEntityTransactions = true` no `ServiceBusClient` e um `TransactionScope`, as duas operações são
atômicas. O broker roteia o envio "via" a primeira entidade da transação (a fila) e só o entrega no tópico quando
a transação é confirmada.

```csharp
using var transacao = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
await args.CompleteMessageAsync(mensagem);      // 1ª operação define a entidade "via"
await sender.SendMessageAsync(eventoApolice);   // sender criado pelo MESMO client
transacao.Complete();
```

Limites, que são perguntas clássicas:

- **Só vale para operações no Service Bus.** Um `INSERT` no SQL Server dentro do mesmo `TransactionScope` não entra
  na transação do broker (não há transação distribuída com o Service Bus). Para banco + mensagem, o padrão é o
  **Transactional Outbox** (fase 4).
- Timeout de 2 minutos. Operações dentro da transação **não têm retry automático** do SDK.
- Não existe no tier Basic.

## Ordem

Repare no log do consumer: a proposta `#33` foi processada antes da `#32`. Com `MaxConcurrentCalls > 1` ou várias
instâncias, **não há garantia de ordem**, nem na fila nem nas assinaturas. Para ordem por apólice, use sessions
(fase 3).

## Infra como código: pegadinha da regra `$Default`

Redefinir a regra `$Default` via Bicep/ARM **não funciona**: o deploy termina com sucesso, mas a assinatura fica
sem nenhuma regra e não recebe nada. A solução usada aqui é dar um nome próprio à regra
(`apolice-sem-revisao`, `requer-revisao`). Uma assinatura criada via ARM já com regra declarada não
ganha a `$Default`. Confira sempre com `dotnet run --project src\Vida.Producer -- regras`.

## Perguntas para revisar

1. **Quando usar fila e quando usar tópico?** Comando para um destinatário → fila. Fato que interessa a várias áreas
   → tópico. Mesmo com um único assinante hoje, publicar um evento em tópico deixa o sistema aberto para novos
   assinantes sem alterar o publisher.
2. **Um assinante novo recebe as mensagens antigas?** Não. Só o que chegar depois da assinatura existir. Se precisar
   de replay, o serviço certo é o Event Hubs (ou guardar os eventos em outro lugar).
3. **Correlation ou SQL filter?** Correlation sempre que for igualdade: é mais rápido e não degrada o tópico. SQL só
   para intervalos e lógica complexa.
4. **Como filtrar por um campo que está no JSON?** Não dá. O publisher precisa copiar o campo para uma application
   property.
5. **O que acontece com uma mensagem que nenhuma assinatura aceita?** É descartada sem erro. Por isso uma assinatura de
   auditoria/catch-all é comum.
6. **Como evitar perder ou duplicar o evento ao processar uma mensagem e publicar outra?** Transação entre entidades do
   Service Bus. Se houver banco no meio, outbox.
7. **Quais os limites?** Até 2.000 assinaturas por tópico (todos os tiers), 2.000 SQL filters e 100.000 correlation
   filters por tópico, 10.000 filas/tópicos por namespace no Standard e até 100 mensagens por transação.
8. **Como escalar um assinante lento?** Mais instâncias na mesma assinatura (competing consumers) e/ou
   `MaxConcurrentCalls` maior. Os outros assinantes não são afetados: cada assinatura tem sua cópia e seu ritmo.

## Referências

- [Tópicos e assinaturas](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-queues-topics-subscriptions#topics-and-subscriptions)
- [Filtros e actions de assinatura](https://learn.microsoft.com/azure/service-bus-messaging/topic-filters)
- [Sintaxe SQL de filtros](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-messaging-sql-filter)
- [Transações e "send via"](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-transactions)
- [Exemplo de transações no SDK .NET](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample06_Transactions.md)
- [Quickstart .NET (tópicos)](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions)
- [Cotas e limites](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-quotas)
