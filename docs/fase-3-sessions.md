# Fase 3 — Sessions, detecção de duplicatas, agendamento e deferral

## O fluxo da POC

```
Producer ──► fila "endossos" (requiresSession + requiresDuplicateDetection)
               SessionId = ApoliceId, MessageId = "{apoliceId}/v{versao}"
                   │
                   ▼
             Vida.Consumer / ProcessadorDeEndossos (ServiceBusSessionProcessor, client próprio)
               session state da apólice: { UltimaVersao, Adiadas: versão → SequenceNumber }
               versão já aplicada  → Complete sem reaplicar
               versão adiantada    → salva estado, Defer
               próxima versão      → aplica, salva estado, Complete, retoma as adiadas da vez

Producer ──► fila "propostas" (simularFalha + retryComBackoff)
             Vida.Consumer / Worker: Complete + Send(cópia com ScheduledEnqueueTime) na mesma transação,
             esperas de 2 s, 4 s...; a dependência simulada volta na 3ª tentativa
```

`endossos` é uma fila nova porque `requiresSession` e `requiresDuplicateDetection` **só podem ser definidos na
criação da entidade**. Tentar mudar numa fila existente dá erro no deploy (`The value for RequiresSession property of
an existing Queue can't be changed`). Para ligar numa entidade que já existe: criar outra e redirecionar o tráfego.

## Glossário comparado

| Azure Service Bus | RabbitMQ | Kafka |
|---|---|---|
| Session (`SessionId`) | Single active consumer / consistent hash exchange | Chave de partição → ordem na partição |
| Session lock (um consumer por sessão) | Single active consumer | Uma partição para um consumer do grupo |
| Session state | Não tem (guarda fora) | Não tem (state store do Kafka Streams) |
| Detecção de duplicatas por `MessageId` | Não tem nativo (plugin de deduplicação) | Producer idempotente (só no envio, por sessão do producer) |
| `ScheduledEnqueueTime` / `ScheduleMessageAsync` | Plugin delayed message exchange | Não tem (tópicos de retry com espera) |
| `DeferMessageAsync` + receive por `SequenceNumber` | Não tem | Não tem (pausar a partição / guardar fora) |

## Sessions: ordem por chave sem limite de partições

- Mensagens com o mesmo `SessionId` chegam **em ordem** e são entregues a **um consumer por vez**: quem aceita a
  sessão trava todas as mensagens dela (session lock), não uma mensagem só.
- Sessões diferentes rodam em paralelo. `MaxConcurrentSessions` (8 aqui) = quantas apólices esta instância atende
  ao mesmo tempo; `MaxConcurrentCallsPerSession = 1` mantém a ordem dentro de cada uma.
- Diferente do Kafka, não há número fixo de partições: um milhão de apólices são um milhão de sessões possíveis, e
  qualquer instância pode pegar qualquer sessão livre. Não existe rebalanceamento de partições.
- `SessionIdleTimeout` (3 s aqui): sem mensagem nova, a instância solta a sessão e pega outra.
- Numa fila com sessão, toda mensagem **precisa** de `SessionId`, e o consumer precisa de um receiver de sessão
  (`CreateSessionProcessor` / `AcceptNextSessionAsync`). Um receiver comum não abre nessa fila.
- **TTL numa sessão:** se uma mensagem da sessão expira, todas as mensagens daquela sessão expiram junto.

### Session state

Um blob opaco guardado pelo broker junto da sessão, do tamanho máximo de uma mensagem (256 KB no Standard).
Qualquer instância que pegar a sessão depois lê o mesmo estado. Na POC ele guarda a última versão aplicada e os
`SequenceNumber` das versões adiadas.

- Persiste até ser limpo (`SetSessionStateAsync(null)`), mesmo com a sessão vazia, e conta na cota da fila.
  Em produção, a "última versão aplicada" mora no banco da apólice; o session state serve para coordenação
  (como os números das adiadas).
- **Não entra em transação com o `Complete`.** Por isso a ordem importa: salvar o estado **antes** do Complete.
  Se o processo cair entre os dois, a mensagem volta e o consumer a reconhece como "já aplicada".

## Ordem de chegada não é ordem de negócio: deferral

Session garante a ordem em que as mensagens **chegaram**. Se o producer enviar v2, v4, v1, v5, v3, elas chegam
nessa ordem. A POC resolve com **defer**:

1. Chegou uma versão adiantada → guarda `versão → SequenceNumber` no session state e chama `DeferMessageAsync`.
2. A mensagem adiada continua na fila, mas **some do receive normal**: só sai com
   `ReceiveDeferredMessagesAsync(sequenceNumber)` (no processor, via `args.GetReceiveActions()`).
3. Quando a versão que faltava é aplicada, o consumer busca as adiadas que ficaram na vez, em sequência.

Log real da POC (ordem de envio v2, v4, v1, v5, v3):

```
endosso v2 chegou antes da v1. Adiado (deferred, #1).
endosso v4 chegou antes da v1. Adiado (deferred, #2).
endosso v1 (Troca de beneficiário) aplicado (#3).
retomando endosso v2 adiado (#1).
endosso v2 (Aumento de capital) aplicado (#1).
endosso v5 chegou antes da v3. Adiado (deferred, #4).
endosso v3 (Inclusão de cobertura) aplicado (#5).
retomando endosso v4 adiado (#2) ... v4 aplicado ... retomando v5 ... v5 aplicado
```

Cuidados:
- **Quem adia precisa guardar o `SequenceNumber`.** Perdeu o número, a mensagem só é achada por peek. Por isso o
  estado é salvo **antes** do defer.
- **Mensagem adiada não expira sozinha**: o TTL só é verificado quando alguém tenta recebê-la pelo número. Uma
  versão que nunca chega deixa as posteriores adiadas para sempre; em produção, um alerta ou um timeout de negócio
  ("v3 não chegou em 1 h") precisa tratar isso.

## Detecção de duplicatas

- O broker guarda os `MessageId` recebidos durante a janela (`duplicateDetectionHistoryTimeWindow`: padrão 10 min,
  mínimo 20 s, máximo 7 dias). Uma mensagem com `MessageId` repetido é **aceita** (o envio dá certo) e
  **descartada** em silêncio.
- Só o `MessageId` é comparado; corpo e propriedades não. Por isso o id precisa vir do negócio:
  `{apoliceId}/v{versao}` é o mesmo em qualquer reenvio. Com um `Guid.NewGuid()` a cada envio, nada seria detectado.
- Resolve o lado do **producer** (reenvio após timeout sem confirmação). Não resolve reentrega ao consumer
  (lock expirado, crash antes do Complete) nem duplicata fora da janela. O consumer continua precisando ser
  idempotente: na POC, a checagem `versao <= UltimaVersao`.
- Janela grande custa vazão: todo envio é comparado com todos os ids guardados.
- Mensagens agendadas também entram na detecção.

Na POC: `endosso-duplicado` envia v1, a mesma v1 de novo e v2. O Producer registra três envios aceitos; o Consumer
aplica v1 e v2 uma vez cada. Os `SequenceNumber` das aplicadas foram #18 e #20: a cópia descartada parece ter
consumido o #19.

## Agendamento: retry com backoff

`Abandon` reentrega na hora (fase 1). Para esperar sem segurar o lock, o consumer:

1. Cria uma cópia da mensagem recebida (`new ServiceBusMessage(recebida)`), com a tentativa + 1 numa propriedade.
2. Define `ScheduledEnqueueTime = agora + 2^tentativa s`.
3. Na mesma transação: `Complete` da atual + envio da cópia para a própria fila.

```
Falha transitória na proposta a85b0c18 (tentativa 1). Nova tentativa agendada para daqui a 2 s.
Falha transitória na proposta a85b0c18 (tentativa 2). Nova tentativa agendada para daqui a 4 s.
Dependência de volta: proposta a85b0c18 processada na tentativa 3.
```

- A cópia é uma **mensagem nova**: `DeliveryCount` volta a 1 e `maxDeliveryCount` não conta as tentativas. O
  contador precisa viajar na mensagem (`tentativa`), e o limite (5) leva para a DLQ com `TentativasEsgotadas`, um
  motivo que **não** é permanente: o reprocessamento da DLQ reenvia.
- `ScheduleMessageAsync` faz o mesmo e devolve o `SequenceNumber`, que permite `CancelScheduledMessageAsync`.
  Mensagem agendada não aparece para consumers até a hora; aparece no peek.
- Outros usos de agendamento: lembrete de vencimento de parcela, início de vigência numa data futura.

## Armadilha encontrada: client com transação entre entidades é exclusivo

O `Vida.Consumer` usa `EnableCrossEntityTransactions` (fase 2). Ao adicionar o processor de endossos no mesmo
client, a sessão não abria:

```
Local transactions cannot span multiple top-level entities such as queue or topic.
```

Com transações entre entidades, **todos os links do client** passam pela primeira entidade que ele tocou (a fila
`propostas`, a "via"). O receiver de `endossos` também tentava passar por ela. Solução: um segundo
`ServiceBusClient`, sem transações, registrado como keyed service (`ProcessadorDeEndossos.ChaveDoClient`). Fica como
exceção à regra "um client por app": o client transacional é exclusivo do fluxo que precisa dele.

## Perguntas para revisar

1. **Como garantir ordem no Service Bus?** Sessions, com `SessionId` = chave de negócio (apólice). Ordem por
   sessão, paralelismo entre sessões, sem limite de partições. Sem sessão não há garantia.
2. **E se o producer mandar fora de ordem?** Session garante a ordem de chegada. Para a ordem de negócio: número de
   versão + defer das adiantadas, com o `SequenceNumber` guardado no session state (ou num banco).
3. **Detecção de duplicatas resolve exactly-once?** Não. Só descarta reenvios do producer dentro da janela. A
   entrega continua at-least-once: o consumer precisa ser idempotente.
4. **Como fazer retry com espera?** Agendar uma cópia com contador de tentativas e completar a original na mesma
   transação. Alternativas: Polly em memória (segurando o lock) ou defer + job que retoma.
5. **Posso ligar sessions numa fila que já existe?** Não: sessions e dedup só na criação. Cria-se outra entidade.
6. **Qual a diferença entre defer e abandon?** Abandon devolve para o receive normal na hora. Defer tira do receive
   normal até alguém pedir pelo `SequenceNumber`.

## Referências

- [Message sessions](https://learn.microsoft.com/azure/service-bus-messaging/message-sessions)
- [Message deferral](https://learn.microsoft.com/azure/service-bus-messaging/message-deferral)
- [Duplicate detection](https://learn.microsoft.com/azure/service-bus-messaging/duplicate-detection)
- [Scheduled messages](https://learn.microsoft.com/azure/service-bus-messaging/message-sequencing#scheduled-messages)
- [Transações (entidade "via")](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-transactions)
- [Erros do Resource Manager (propriedades imutáveis)](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-resource-manager-exceptions)
