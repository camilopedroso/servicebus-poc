# Fase 1 — Filas no Azure Service Bus

## O modelo mental em uma frase

Service Bus é um **broker de mensagens corporativo**, mais parecido com o RabbitMQ do que com o Kafka:
a mensagem **sai do broker quando é confirmada** (não existe log com offset para reler).
O "Kafka da Azure" é o **Event Hubs**, que inclusive aceita o protocolo Kafka.

## Glossário comparado

| Azure Service Bus | RabbitMQ | Kafka |
|---|---|---|
| Namespace (`*.servicebus.windows.net`) | Virtual host / cluster | Cluster |
| Fila (queue) | Fila | Tópico com 1 consumer group |
| Tópico + assinaturas (fase 2) | Exchange + filas ligadas por binding | Tópico + vários consumer groups |
| Filtro de assinatura (SQL/correlação) | Binding key / headers exchange | Não tem (filtra no consumer) |
| Sessão (fase 3) | Single active consumer | Partition key → ordem por partição |
| `ServiceBusClient` (1 por app) | `IConnection` | `KafkaProducer`/`Consumer` |
| `ServiceBusSender` / `Receiver` / `Processor` | Channel + publish / consume | Producer / Consumer |
| Peek-lock + `Complete` | `autoAck=false` + `BasicAck` | Commit de offset |
| `Abandon` | `BasicNack(requeue: true)` | Não commitar / seek |
| `DeadLetter` + DLQ automática por entidade | Dead-letter exchange (configurar) | Tópico DLT (implementa você) |
| `ReceiveAndDelete` | `autoAck=true` | `enable.auto.commit` antes de processar |
| `MaxConcurrentCalls` / `PrefetchCount` | `BasicQos(prefetch)` | `max.poll.records` |
| `ApplicationProperties` | Headers | Headers |
| Protocolo AMQP 1.0 (porta 5671 ou WebSockets 443) | AMQP 0-9-1 | Protocolo próprio sobre TCP |

## Conceitos exercitados na POC

### Peek-lock (`ReceiveMode.PeekLock`)
O consumer recebe a mensagem **travada**: ela fica invisível para os outros por `lockDuration`
(30 s na nossa fila, máx. 5 min). O consumer precisa resolver a mensagem (*settlement*):

| Ação | Efeito | Quando usar na POC |
|---|---|---|
| `CompleteMessageAsync` | Remove da fila | Proposta processada |
| `AbandonMessageAsync` | Solta o lock, volta **na hora** para a fila, `DeliveryCount++` | Falha transitória |
| `DeadLetterMessageAsync(reason, description)` | Move para a DLQ com motivo | Regra de negócio / payload inválido |
| `DeferMessageAsync` | Fica guardada, só sai lendo pelo `SequenceNumber` | Fase 3 |
| (lock expira) | Mesmo efeito do Abandon | Consumer caiu ou travou |

Garantia: **at-least-once**. Se o consumer processar e cair antes do `Complete`, a mensagem volta → o
consumer precisa ser **idempotente** (fase 3).

`ReceiveAndDelete` remove a mensagem na entrega: mais rápido, mas **at-most-once** (perde se o consumer cair).

### Retry e DLQ
- `maxDeliveryCount` (5 aqui, padrão 10): ao atingir, o **broker** move a mensagem para
  `propostas/$DeadLetterQueue` com `DeadLetterReason = MaxDeliveryCountExceeded`.
- Expiração por TTL com `deadLetteringOnMessageExpiration = true` → DLQ com `TTLExpiredException`.
- `DeadLetterMessageAsync` → DLQ com o motivo que **você** escolhe (`RegraDeNegocio`, `PayloadInvalido`).
- A DLQ **não expira** e não é limpa sozinha: alguém precisa monitorar (alerta de métrica) e tratar.
- Reprocessar = ler da DLQ, reenviar para a fila principal e só então completar na DLQ
  (ordem importa: no pior caso duplica, nunca perde).

**Ponto de atenção:** `Abandon` reentrega imediatamente, **sem backoff**.
Para retry com espera, os padrões comuns são:
1. Agendar uma cópia (`ScheduleMessageAsync` / `ScheduledEnqueueTime`) com um contador de tentativas e completar a original.
2. Retry em memória (Polly) dentro do handler, respeitando o lock (o SDK renova até `MaxAutoLockRenewalDuration`).
3. `Defer` + reprocessamento agendado.

### Competing consumers
Várias instâncias lendo a mesma fila dividem as mensagens: é assim que se escala horizontalmente.
Diferente do Kafka, **não há limite por número de partições**: dá para ter 50 consumers numa fila.
O preço: **sem ordem garantida** (abandons e paralelismo embaralham). Ordem estrita = sessões (fase 3).

### Envio em lote
`CreateMessageBatchAsync` + `TryAddMessage` agrupa mensagens numa única chamada, respeitando o limite de
tamanho do tier (**256 KB no Standard**, **até 100 MB no Premium**). Payload grande → *claim check pattern*
(guarda no Blob Storage e manda só a referência).

### Autenticação sem segredo
- `disableLocalAuth: true` no namespace → connection strings/SAS **não funcionam**.
- O app usa `DefaultAzureCredential`: localmente usa o `az login`; no Azure, a **Managed Identity**.
- Permissão via RBAC no *data plane*: `Azure Service Bus Data Sender`, `Data Receiver` ou `Data Owner`.
  Na POC demos `Data Owner` ao seu usuário; em produção, cada app recebe só o papel de que precisa (least privilege).

### Boas práticas de cliente
- `ServiceBusClient` é caro (conexão AMQP) e thread-safe → **singleton**. Senders/receivers são leves, mas também reutilizáveis.
- `ServiceBusProcessor` cuida do loop de recebimento, concorrência e renovação de lock; use `ProcessErrorAsync` para logar falhas de infraestrutura.
- `ServiceBusRetryOptions` cobre falhas **transitórias de comunicação** (rede, throttling), não exceções do seu código.
- `ServiceBusException.Reason` / `IsTransient` ajudam a decidir o que fazer com erros.

## Tiers (pergunta frequente)

| | Basic | Standard | Premium |
|---|---|---|---|
| Filas | ✅ | ✅ | ✅ |
| Tópicos/assinaturas | ❌ | ✅ | ✅ |
| Sessões, transações, dedup | ❌ | ✅ | ✅ |
| Mensagem máx. | 256 KB | 256 KB | 100 MB |
| Recursos | Compartilhados | Compartilhados | Dedicados (Messaging Units), VNet/Private Endpoint, geo-DR |
| Custo (brazilsouth) | por operação | ~US$ 10/mês + operações | ~US$ 0,93/h por MU |

Em empresa de seguros com exigência de compliance, Premium costuma aparecer por causa de **Private Endpoint**
e isolamento.

## Perguntas para revisar

1. **Fila ou tópico?** Fila = um destinatário lógico (comando: "analise esta proposta"). Tópico = vários
   interessados independentes (evento: "apólice emitida" → cobrança, notificação, BI).
2. **Como garantir que nada se perde?** Peek-lock + complete só após processar + DLQ monitorada +
   outbox no lado do producer (fase 4).
3. **E duplicatas?** At-least-once → consumer idempotente (chave de negócio, tabela de mensagens processadas)
   e/ou detecção de duplicatas do broker por `MessageId` (fase 3).
4. **Como garantir ordem?** Sessões (`SessionId` = id da apólice). Sem sessão não há garantia.
5. **Diferença entre Service Bus, Event Hubs e Event Grid?**
   Service Bus = mensagens de negócio de alto valor (transação, ordem, DLQ).
   Event Hubs = streaming de alto volume/telemetria, retenção e replay (≈ Kafka).
   Event Grid = notificação de eventos reativa (push HTTP), ex.: "blob criado".
6. **Mensagem foi para a DLQ, e agora?** Ver `DeadLetterReason`, corrigir a causa, reprocessar (com ferramenta
   como o `reprocessar-dlq`), alertar pela métrica `DeadletteredMessages`.
7. **O que acontece se o processamento demorar mais que o lock?** O lock expira, outra instância pega a mensagem
   (processamento duplicado) e o `Complete` da primeira falha com `MessageLockLost`. Solução: renovação
   automática (`MaxAutoLockRenewalDuration`), aumentar `lockDuration` ou quebrar o trabalho.
8. **Por que não usar connection string?** Segredo vaza, precisa de rotação. Managed Identity + RBAC não tem segredo.

## Referências

- [Visão geral do Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-messaging-overview)
- [Peek-lock e settlement](https://learn.microsoft.com/azure/service-bus-messaging/message-transfers-locks-settlement)
- [Dead-letter queues](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dead-letter-queues)
- [Quickstart .NET (filas)](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dotnet-get-started-with-queues)
- [Autenticação com Entra ID](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-managed-service-identity)
- [Cotas e limites](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-quotas)
- [Comparação Service Bus x Event Hubs x Event Grid](https://learn.microsoft.com/azure/service-bus-messaging/compare-messaging-services)
- SDK: [Azure.Messaging.ServiceBus 7.21.0](https://www.nuget.org/packages/Azure.Messaging.ServiceBus/7.21.0)
