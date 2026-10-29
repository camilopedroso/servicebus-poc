// Squad de Plataforma: o que todas as squads usam e ninguém publica. Namespace Service Bus, RBAC e
// observabilidade (Application Insights). Os tópicos, filas e assinaturas ficam nos módulos de cada serviço.
// Standard é o tier mínimo com tópicos/assinaturas (fase 2); Basic só tem filas.

@description('Região dos recursos.')
param location string

@description('Object ID do usuário (Entra ID) que vai rodar os apps localmente.')
param developerPrincipalId string

// Nome do namespace precisa ser globalmente único (vira <nome>.servicebus.windows.net).
var namespaceName = 'sb-vida-poc-${uniqueString(resourceGroup().id)}'

// Role built-in "Azure Service Bus Data Owner" (send + receive + manage no data plane).
// Em produção, prefira Data Sender / Data Receiver separados por aplicação (least privilege),
// com escopo na entidade de cada serviço, e não no namespace.
var serviceBusDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

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

output namespaceName string = namespace.name
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
// Não é segredo com DisableLocalAuth: sem token Entra ID, a ingestão é recusada.
output appInsightsConnectionString string = appInsights.properties.ConnectionString
