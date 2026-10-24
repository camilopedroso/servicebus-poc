// Infraestrutura da POC: namespace Service Bus (Standard) + fila de propostas.
// Standard é o tier mínimo com tópicos/assinaturas (fase 2); Basic só tem filas.

@description('Região dos recursos.')
param location string = resourceGroup().location

@description('Object ID do usuário (Entra ID) que vai rodar producer/consumer localmente.')
param developerPrincipalId string

// Tempo que o consumer tem para concluir antes do lock expirar e a mensagem voltar à fila/assinatura.
// É configuração da ENTIDADE no broker (o cliente não escolhe). Máximo: 5 min.
// up.ps1 -ModoDebug usa PT5M para dar tempo de parar em breakpoints.
@description('lockDuration da fila e das assinaturas (ISO 8601, máx. PT5M).')
param lockDuration string = 'PT30S'

// Nome do namespace precisa ser globalmente único (vira <nome>.servicebus.windows.net).
var namespaceName = 'sb-vida-poc-${uniqueString(resourceGroup().id)}'

// Role built-in "Azure Service Bus Data Owner" (send + receive + manage no data plane).
// Em produção, prefira Data Sender / Data Receiver separados por aplicação (least privilege).
var serviceBusDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

// Criação do namespace Service Bus e configuração de RBAC para o desenvolvedor.
resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // Desliga SAS/connection strings: só Entra ID (RBAC) autentica.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

// Criação da fila de propostas no namespace Service Bus.
resource propostas 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'propostas'
  properties: {
    lockDuration: lockDuration
    // Após 5 entregas sem Complete, a mensagem vai para a DLQ ($DeadLetterQueue).
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P14D'
    // Mensagens expiradas também vão para a DLQ em vez de sumir.
    deadLetteringOnMessageExpiration: true
  }
}

// Criação do tópico "apolice-emitida" no namespace Service Bus.
resource apoliceEmitida 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: 'apolice-emitida'
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

// Cada assinatura é uma "fila virtual" com cópia própria das mensagens que passam no filtro,
// com lock, maxDeliveryCount e DLQ independentes (≈ consumer group do Kafka / fila ligada a um exchange).
var propriedadesAssinatura = {
  lockDuration: lockDuration
  maxDeliveryCount: 5
  deadLetteringOnMessageExpiration: true
  // Mensagem que dá erro ao avaliar o filtro (ex.: tipo errado) vai para a DLQ em vez de sumir.
  deadLetteringOnFilterEvaluationExceptions: true
}


// Criação das assinaturas do tópico "apolice-emitida" com suas respectivas regras e filtros.
resource cobranca 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'cobranca'
  properties: propriedadesAssinatura
}
// Uma assinatura criada sem regras ganha a regra $Default (TrueFilter = recebe tudo). Se houver várias regras,
// vale o OR entre elas. Obs.: redefinir '$Default' via ARM não funciona (o deploy passa, mas a regra some);
// por isso cada filtro tem nome próprio.
resource cobrancaRegra 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: cobranca
  name: 'apenas-apolice-emitida'
  properties: {
    // Correlation filter: igualdade em propriedades, avaliado por hash. Mais barato que SQL.
    filterType: 'CorrelationFilter'
    correlationFilter: {
      label: 'ApoliceEmitida' // "label" é o nome antigo de Subject
    }
  }
}


// Sem regra customizada: fica com $Default (TrueFilter) e recebe todos os eventos do tópico.
resource notificacao 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'notificacao'
  properties: propriedadesAssinatura
}


// Criação da assinatura "alto-valor" com filtro para capital segurado acima de 1 milhão.
resource altoValor 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'alto-valor'
  properties: propriedadesAssinatura
}
// Criação da regra para a assinatura "alto-valor" que aplica o filtro SQL e a action de prioridade alta.
resource altoValorRegra 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: altoValor
  name: 'capital-acima-de-1-milhao'
  properties: {
    // SQL filter: expressões sobre propriedades (nunca sobre o corpo). Mais flexível, mais caro de avaliar.
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'capitalSegurado > 1000000'
    }
    // Action: altera as propriedades da cópia que entra nesta assinatura (as outras não veem).
    action: {
      sqlExpression: 'SET prioridade = \'alta\''
    }
  }
}

// Tópico "proposta-recusada": recusa por regra de negócio é um resultado do processamento, não uma falha.
// O consumer conclui a proposta e publica o fato aqui; a DLQ fica só para o que não dá para processar.
resource propostaRecusada 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: 'proposta-recusada'
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

// Sem regra customizada: fica com $Default (TrueFilter) e recebe todas as recusas para avisar o cliente.
resource notificacaoRecusa 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: propostaRecusada
  name: 'notificacao-recusa'
  properties: propriedadesAssinatura
}

// Atribuição de função para o desenvolvedor como Data Owner no namespace Service Bus.
resource developerDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, developerPrincipalId, serviceBusDataOwnerRoleId)
  scope: namespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleId)
    principalId: developerPrincipalId
    principalType: 'User'
  }
}

// Outputs com informações do namespace Service Bus criado.
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
