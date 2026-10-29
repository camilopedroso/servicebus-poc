// Squad de Propostas, o que ela ASSINA: os desfechos da Subscrição voltam para a API, que atualiza o status
// da proposta (Aprovada ou Recusada) no próprio banco. É assim que um serviço sabe do outro: por evento, nunca
// lendo o banco alheio nem chamando a API dele no caminho de escrita.

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

// Sem filtro: eventos de propostas que não vieram da API (demonstração) são descartados pelo serviço,
// que não as encontra no banco. Uma assinatura por consumidor: cada um recebe a sua cópia.
resource statusAprovada 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'status-aprovada'
  properties: propriedadesDeAssinatura(lockDuration)
}

resource statusRecusada 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: propostaRecusada
  name: 'status-recusada'
  properties: propriedadesDeAssinatura(lockDuration)
}
