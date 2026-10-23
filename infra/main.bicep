// Infraestrutura da POC: namespace Service Bus (Standard) + fila de propostas.
// Standard é o tier mínimo com tópicos/assinaturas (fase 2); Basic só tem filas.

@description('Região dos recursos.')
param location string = resourceGroup().location

@description('Object ID do usuário (Entra ID) que vai rodar producer/consumer localmente.')
param developerPrincipalId string

// Nome do namespace precisa ser globalmente único (vira <nome>.servicebus.windows.net).
var namespaceName = 'sb-vida-poc-${uniqueString(resourceGroup().id)}'

// Role built-in "Azure Service Bus Data Owner" (send + receive + manage no data plane).
// Em produção, prefira Data Sender / Data Receiver separados por aplicação (least privilege).
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

resource propostas 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'propostas'
  properties: {
    // Tempo que o consumer tem para concluir antes do lock expirar e a mensagem voltar à fila.
    lockDuration: 'PT30S'
    // Após 5 entregas sem Complete, a mensagem vai para a DLQ ($DeadLetterQueue).
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P14D'
    // Mensagens expiradas também vão para a DLQ em vez de sumir.
    deadLetteringOnMessageExpiration: true
  }
}

resource developerDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, developerPrincipalId, serviceBusDataOwnerRoleId)
  scope: namespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleId)
    principalId: developerPrincipalId
    principalType: 'User'
  }
}

output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
