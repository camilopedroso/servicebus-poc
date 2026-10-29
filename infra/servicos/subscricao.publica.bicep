// Squad de Subscrição, o que ela PUBLICA ou RECEBE NA SUA CAIXA: os tópicos dos desfechos (apolice-emitida,
// proposta-recusada) e as filas próprias (subscricao-entrada, endossos). As assinaturas ficam com quem consome.

@description('Namespace criado por plataforma.bicep.')
param namespaceName string

// Tempo que o consumer tem para concluir antes do lock expirar e a mensagem voltar à fila/assinatura.
// É configuração da ENTIDADE no broker (o cliente não escolhe). Máximo: 5 min.
// up.ps1 -ModoDebug usa PT5M para dar tempo de parar em breakpoints.
@description('lockDuration das filas (ISO 8601, máx. PT5M).')
param lockDuration string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: namespaceName
}

// Caixa de entrada da Subscrição: a fila continua existindo, mas é DELA, não da API. A retentativa agendada
// (backoff) e o reprocessamento da DLQ voltam para cá; se voltassem para o tópico, iriam para todas as assinaturas.
// A assinatura que a alimenta (forwardTo) fica em subscricao.assina.bicep.
resource subscricaoEntrada 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'subscricao-entrada'
  properties: {
    lockDuration: lockDuration
    // Após 5 entregas sem Complete, a mensagem vai para a DLQ ($DeadLetterQueue).
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P14D'
    // Mensagens expiradas também vão para a DLQ em vez de sumir.
    deadLetteringOnMessageExpiration: true
  }
}

// Fase 3: fila de endossos (alterações numa apólice já emitida, que precisam ser aplicadas em ordem).
// requiresSession e requiresDuplicateDetection só podem ser definidos na CRIAÇÃO da entidade: mudar depois
// dá erro no deploy. Por isso é uma fila própria, e não a de entrada com essas opções ligadas.
resource endossos 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'endossos'
  properties: {
    lockDuration: lockDuration
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    // Sessions: mensagens com o mesmo SessionId (a apólice) são entregues em ordem, a um consumer por vez.
    requiresSession: true
    // O broker guarda os MessageIds recebidos nesta janela e descarta (em silêncio) quem repetir.
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

// Evento publicado quando a subscrição aprova uma proposta. Assinam: Cobrança, Notificação, Resseguro e Propostas.
resource apoliceEmitida 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: 'apolice-emitida'
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

// Recusa por regra de negócio é um resultado do processamento, não uma falha.
// O consumer conclui a proposta e publica o fato aqui; a DLQ fica só para o que não dá para processar.
resource propostaRecusada 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: 'proposta-recusada'
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

output filaDeEntrada string = subscricaoEntrada.name
output filaDeEndossos string = endossos.name
output topicoApoliceEmitida string = apoliceEmitida.name
output topicoPropostaRecusada string = propostaRecusada.name
