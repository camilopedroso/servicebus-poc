// Squad de Notificação, o que ela ASSINA: avisa o cliente de toda apólice emitida e de toda recusa.
// Uma squad pode assinar tópicos de mais de um publicador; aqui os dois são da Subscrição.

import { propriedadesDeAssinatura } from 'padroes.bicep'

@description('Namespace criado por plataforma.bicep.')
param namespaceName string

@description('lockDuration das assinaturas (ISO 8601, máx. PT5M).')
param lockDuration string

@description('Tópico publicado por subscricao.publica.bicep.')
param topicoApoliceEmitida string

@description('Tópico publicado por subscricao.publica.bicep.')
param topicoPropostaRecusada string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: namespaceName
}

resource apoliceEmitida 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' existing = {
  parent: namespace
  name: topicoApoliceEmitida
}

resource propostaRecusada 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' existing = {
  parent: namespace
  name: topicoPropostaRecusada
}

// Sem regra customizada: fica com $Default (TrueFilter) e recebe todos os eventos do tópico.
resource notificacao 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'notificacao'
  properties: propriedadesDeAssinatura(lockDuration)
}

// Sem regra customizada: fica com $Default (TrueFilter) e recebe todas as recusas para avisar o cliente.
resource notificacaoRecusa 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: propostaRecusada
  name: 'notificacao-recusa'
  properties: propriedadesDeAssinatura(lockDuration)
}
