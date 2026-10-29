# Fase 4 — API REST e Transactional Outbox no SQL Server

## O problema: dual write

Uma API que recebe uma proposta precisa fazer duas coisas: **gravar no banco** e **publicar no Service Bus**.
São dois sistemas, sem transação entre eles. A documentação do Service Bus é explícita: código que acessa banco
de dados **não entra** no escopo transacional do broker. As transações do Service Bus (fase 2) cobrem só operações
no próprio broker.

| Ordem | Se cair entre as duas | Resultado |
|---|---|---|
| Grava, depois publica | Gravou, não publicou | Proposta parada para sempre: ninguém analisa |
| Publica, depois grava | Publicou, não gravou | Mensagem de uma proposta que não existe no banco |

O cenário **"Sem outbox: dual write com falha"** mostra o primeiro caso: a proposta fica em `Recebida`, e o
Consumer nunca recebe nada.

## A solução: Transactional Outbox

```
POST /api/propostas
   │  uma transação local do SQL Server (um SaveChanges)
   ├─► INSERT Propostas          (o dado de negócio)
   └─► INSERT OutboxMensagens    (a mensagem, serializada)
   │
   └─► 201 Created + Location     ← a API não falou com o Service Bus

DespachanteDoOutbox (BackgroundService, a cada 1 s)
   BEGIN TRAN
     SELECT TOP 20 ... WITH (UPDLOCK, READPAST, ROWLOCK) WHERE PublicadaEm IS NULL ORDER BY Id
     SendMessagesAsync(lote)  ──► fila "propostas" ──► Vida.Consumer (como nas fases anteriores)
     UPDATE PublicadaEm, Propostas.Status = EnviadaParaAnalise
   COMMIT
```

- O dado e a mensagem ficam **no mesmo banco**, então uma transação local garante "os dois ou nenhum". Não precisa
  de transação distribuída (2PC/MSDTC), que o Service Bus não suporta.
- A mensagem é montada pela mesma fábrica do envio direto (`MensagensProposta.Criar`) e guardada como linha
  (`MensagemDeSaida.De`): corpo, `MessageId`, `Subject`, `CorrelationId` e as application properties em JSON.
- O despachante publica depois, com **at-least-once**: se publicar e cair antes do `COMMIT`, a linha continua
  pendente e sai de novo, com o **mesmo `MessageId`**.

## Detalhes que fazem diferença

### Várias instâncias do despachante
`UPDLOCK` trava as linhas lidas até o fim da transação; `READPAST` faz outra instância **pular** as travadas em vez de
esperar. Cada instância pega um lote diferente, sem coordenação externa. Custo: a transação do banco fica aberta
durante o envio ao Service Bus. Com lote pequeno (20), é aceitável. Alternativa: marcar as linhas como "em envio"
com prazo, commitar, enviar e marcar de novo (duas transações curtas, mais estados para tratar).

### Ordem
O `Id` (identity) dá a ordem de gravação, e o lote vai em uma chamada só, que preserva a ordem. Com várias instâncias,
lotes diferentes podem sair intercalados. Se a ordem por agregado importar, combine com sessions (fase 3).

### Duplicatas
A fila `propostas` não tem detecção de duplicatas (fase 3), então um reenvio do despachante chegaria duas vezes ao
Consumer. As saídas: fila com `requiresDuplicateDetection` (o `MessageId` estável do outbox foi feito para isso)
e/ou consumer idempotente. Em produção, as duas.

### Indisponibilidade do Service Bus
Cenário **"Service Bus fora do ar"**: o despachante falha, incrementa `Tentativas` e grava `ProximaTentativaEm` (2 s,
4 s... até 10 s). A API continua respondendo **201**, porque o compromisso dela é com o banco. Ao restabelecer, as
pendentes saem na ordem de gravação:

```
Outbox: 2 mensagem(ns) para 'propostas' não publicada(s): ServiceCommunicationProblem. Próxima tentativa em 4 s.
Outbox: mensagem 8a5ef22e... publicada em 'propostas' (7291 ms depois de gravada, tentativa 3).
```

O outbox troca **disponibilidade** por **latência**: a proposta é aceita mesmo com o broker fora, e chega depois.

### Limpeza
Linhas publicadas não são apagadas na POC. Em produção, um job remove (ou arquiva) as publicadas há mais de N dias,
senão a tabela só cresce.

### Polling × CDC
Polling a cada segundo é simples e suficiente aqui. Alternativas com menos latência e menos consultas: Change Data
Capture do SQL Server, ou bibliotecas que já trazem o outbox (MassTransit, NServiceBus, Wolverine).

## A API REST

| Endpoint | Resposta |
|---|---|
| `POST /api/propostas` | `201 Created` + `Location: /api/propostas/{id}`; `400` com erros por campo (ProblemDetails); `200` se a `Idempotency-Key` já foi usada |
| `GET /api/propostas/{id}` | `200` com `status` (`Recebida` → `EnviadaParaAnalise` → `Aprovada` ou `Recusada`), `apoliceId` ou `motivoDaRecusa`; `404` ProblemDetails |
| `/openapi/v1.json` | Contrato OpenAPI gerado pelo ASP.NET Core, com os comentários `///` como summary/description (só em Development) |
| `/openapi/demo.json` | Contrato dos endpoints de demonstração (`/demo/...`), separado do v1 (só em Development) |
| `/scalar` | Interface para explorar e testar os dois contratos (Scalar; só em Development) |

### Idempotency-Key
O cliente que não recebeu a resposta (timeout) não sabe se a proposta foi criada. Com o cabeçalho
`Idempotency-Key`, ele pode repetir o POST sem medo:

- Primeira vez: grava e responde `201`. A chave fica numa coluna com **índice único filtrado** (só entre quem mandou
  chave).
- Repetição: encontra a chave e devolve `200` com a mesma proposta e `Idempotent-Replayed: true`. Nada é gravado
  nem publicado de novo.
- Corrida (dois POSTs simultâneos com a mesma chave): o índice único deixa um só passar; o outro recebe a violação
  (erro 2601/2627), busca a proposta e responde como repetição.
- Não implementado: comparar o corpo da repetição com o original (APIs como a do Stripe respondem erro se o corpo
  mudar) e expirar chaves antigas.

### Por que 201 e não 202?
A API **criou** o recurso (está no banco e pode ser consultado no `Location`); o que é assíncrono é a análise.
`202 Accepted` seria mais adequado se nada fosse gravado antes de responder. Os dois são defensáveis; o importante é
ter o `Location` para o cliente acompanhar o status.

### O desfecho volta por eventos
A API responde 201 para uma proposta de 93 anos: o dado é válido, e recusar por idade é decisão da subscrição
(análise de risco), não validação de formato. A decisão acontece depois, no `Vida.Consumer`, que publica
`ApoliceEmitida` ou `PropostaRecusada`. O Consumer não conhece o banco da API, e nem deve: o dono do dado é o
Producer.

Por isso o Producer é mais um assinante dos dois tópicos, com assinaturas próprias (`status-aprovada` e
`status-recusada`, sem filtro). O `ProcessadorDeDesfechos` grava `Aprovada` (com a apólice) ou `Recusada` (com o
motivo), e o `GET` passa a contar a história completa. Detalhes:

- **Evento repetido** (at-least-once): se a proposta já tem desfecho, a mensagem só é concluída.
- **Proposta desconhecida**: os endpoints de demonstração mandam direto para a fila, sem gravar no banco. O evento
  volta, o Producer não acha a proposta e conclui a mensagem sem fazer nada.
- **Corrida com o despachante**: a recusa é rápida, e o evento pode voltar entre o envio e o UPDATE que marca
  `EnviadaParaAnalise`. O UPDATE só tira a proposta de `Recebida`, então não sobrescreve o desfecho.
- Sem SQL configurado o processor nem sobe: os eventos ficam guardados nas assinaturas (TTL de 14 dias) em vez de
  falhar até a DLQ.

No Kafka seria o mesmo desenho: um consumer group a mais nos tópicos de resultado. No RabbitMQ, uma fila a mais
ligada ao exchange.

## Decisões da POC

- **SQL Server em Docker** (`infra/sql-up.ps1`, porta 14330), não no Azure: custo zero. A senha é gerada na primeira
  execução e fica só em `src/sqlserver.local.json` (ignorado pelo git).
- **EF Core 10 com migrations** (`Dados/Migrations`), aplicadas na partida do Producer. Em produção, a migration roda
  no pipeline (script idempotente ou bundle), não no app: várias instâncias migrando ao mesmo tempo é problema.
- **Pacote `System.Security.Cryptography.Pkcs` fixado em 10.0.13**: o `Microsoft.Data.SqlClient` 6.1.7 trazia a 9.0.11,
  com vulnerabilidade alta (GHSA-846f-f764-76mx). A referência direta sobe a versão transitiva.
- **API dentro do `Vida.Producer`**: reaproveita a coluna dele no painel e deixa o envio direto (fases 1 a 3) lado a
  lado com o outbox, que é o contraste da fase.

## Perguntas para revisar

1. **Como garantir que gravar e publicar aconteçam juntos?** Transactional outbox: mensagem na mesma transação do
   dado, publicador separado. Nada de 2PC com o broker.
2. **O outbox garante exactly-once?** Não: at-least-once na publicação. O `MessageId` estável permite deduplicar no
   broker (detecção de duplicatas) e no consumer (idempotência).
3. **E o lado do consumer?** O inverso: *inbox* (tabela de mensagens processadas, gravada na mesma transação do
   efeito) ou efeito idempotente por chave de negócio.
4. **Como escalar o publicador?** Várias instâncias com `UPDLOCK, READPAST`, ou particionar as linhas por chave.
5. **O que acontece se o Service Bus cair?** A API continua aceitando; o outbox acumula e drena quando ele volta.
   Monitorar a quantidade de pendentes e a idade da mais antiga (alerta).
6. **POST idempotente?** POST não é idempotente por definição; a `Idempotency-Key` torna seguro repetir.

## Referências

- [Transações no Service Bus (o que entra e o que não entra no escopo)](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-transactions)
- [Transactional outbox pattern (Azure Architecture Center, com Cosmos DB)](https://learn.microsoft.com/azure/architecture/databases/guide/transactional-outbox-cosmos)
- [Processamento idempotente de mensagens](https://learn.microsoft.com/azure/architecture/reference-architectures/containers/aks-mission-critical/mission-critical-data-platform#idempotent-message-processing)
- [Migrations do EF Core em produção](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying)
- [OpenAPI no ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview)
- [Table hints: READPAST, UPDLOCK](https://learn.microsoft.com/sql/t-sql/queries/hints-transact-sql-table)
