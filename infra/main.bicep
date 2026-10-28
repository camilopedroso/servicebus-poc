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

// Fase 3: fila de endossos (alterações numa apólice já emitida, que precisam ser aplicadas em ordem).
// requiresSession e requiresDuplicateDetection só podem ser definidos na CRIAÇÃO da entidade: mudar depois
// dá erro no deploy. Por isso é uma fila nova, e não a "propostas" com essas opções ligadas.
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
// Renomear uma regra aqui NÃO apaga a antiga no broker (deploy incremental): ela continua valendo em OR.
// Ao renomear, apague a antiga: az servicebus topic subscription rule delete ... -n <nome-antigo>.
resource cobrancaRegra 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: cobranca
  name: 'apolice-sem-revisao'
  properties: {
    // Apólice em revisão manual não é cobrada agora. O limite de capital fica no código (PoliticaDeRevisao);
    // aqui só se roteia pela decisão já tomada.
    // Seria um correlation filter (mais barato), mas via ARM as propriedades dele são sempre string:
    // 'false' (string) não casa com o bool que o publisher envia. Em SQL, FALSE é booleano de verdade.
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'sys.Label = \'ApoliceEmitida\' AND requerRevisao = FALSE' // sys.Label = Subject
    }
  }
}


// Sem regra customizada: fica com $Default (TrueFilter) e recebe todos os eventos do tópico.
resource notificacao 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'notificacao'
  properties: propriedadesAssinatura
}


// Criação da assinatura "alto-valor": apólices que exigem revisão manual e resseguro.
resource altoValor 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'alto-valor'
  properties: propriedadesAssinatura
}
// Criação da regra para a assinatura "alto-valor" que aplica o filtro SQL e a action de prioridade alta.
resource altoValorRegra 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: altoValor
  name: 'requer-revisao'
  properties: {
    // SQL filter: expressões sobre propriedades (nunca sobre o corpo). Mais flexível, mais caro de avaliar.
    // Complemento exato do filtro da cobranca: cada apólice cai em uma das duas, nunca nas duas.
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'requerRevisao = TRUE'
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

// Assinaturas do Vida.Producer: o desfecho da subscrição volta para a API, que atualiza o status da proposta
// (Aprovada ou Recusada). Sem filtro: eventos de propostas que não vieram da API (demonstração) são descartados
// pelo Producer, que não as encontra no banco. Uma assinatura por consumidor: cada um recebe a sua cópia.
resource statusAprovada 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'status-aprovada'
  properties: propriedadesAssinatura
}

resource statusRecusada 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: propostaRecusada
  name: 'status-recusada'
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

// Fase 5: observabilidade. Application Insights "workspace-based": a telemetria fica num Log Analytics workspace,
// que é quem cobra (por GB ingerido; 5 GB/mês grátis por conta de cobrança).
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-vida-poc-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // Teto de ingestão por dia: trava de custo para a POC (atingido o teto, a ingestão para até o dia seguinte).
    workspaceCapping: { dailyQuotaGb: json('0.1') }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-vida-poc-${uniqueString(resourceGroup().id)}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // Como no Service Bus: só Entra ID. A connection string identifica o recurso, mas não basta para enviar.
    DisableLocalAuth: true
  }
}

// "Monitoring Metrics Publisher": apesar do nome, publica toda a telemetria (traces, logs, métricas).
var monitoringMetricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'

resource developerTelemetria 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appInsights.id, developerPrincipalId, monitoringMetricsPublisherRoleId)
  scope: appInsights
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisherRoleId)
    principalId: developerPrincipalId
    principalType: 'User'
  }
}

// Outputs com informações do namespace Service Bus criado.
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
// Não é segredo com DisableLocalAuth: sem token Entra ID, a ingestão é recusada.
output appInsightsConnectionString string = appInsights.properties.ConnectionString
