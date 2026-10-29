// Infraestrutura da POC, como orquestrador: cada squad tem o seu módulo em servicos/.
// Regra de dono: quem PUBLICA cria o tópico (e as filas que recebe); quem CONSOME cria a sua assinatura.
//
// Há um ciclo escondido: Propostas assina tópicos da Subscrição (status-*), e a Subscrição assina o tópico de
// Propostas. Com um módulo por serviço, um dependeria do outro. Por isso cada serviço tem dois: *.publica.bicep
// (tópicos e filas) e *.assina.bicep (assinaturas). Todos os "publica" sobem antes de qualquer "assina".
// A ordem não é declarada à mão: cada "assina" recebe por parâmetro o nome que um "publica" devolve como output,
// e essa referência já cria a dependência no deploy.

@description('Região dos recursos.')
param location string = resourceGroup().location

@description('Object ID do usuário (Entra ID) que vai rodar os apps localmente.')
param developerPrincipalId string

// Tempo que o consumer tem para concluir antes do lock expirar e a mensagem voltar à fila/assinatura.
// É configuração da ENTIDADE no broker (o cliente não escolhe). Máximo: 5 min.
// up.ps1 -ModoDebug usa PT5M para dar tempo de parar em breakpoints.
@description('lockDuration das filas e das assinaturas (ISO 8601, máx. PT5M).')
param lockDuration string = 'PT30S'

// Plataforma: namespace, RBAC, Application Insights.
module plataforma 'servicos/plataforma.bicep' = {
  name: 'plataforma'
  params: {
    location: location
    developerPrincipalId: developerPrincipalId
  }
}

var namespaceName = plataforma.outputs.namespaceName

// O que cada squad publica.
module propostasPublica 'servicos/propostas.publica.bicep' = {
  name: 'propostas-publica'
  params: {
    namespaceName: namespaceName
  }
}

module subscricaoPublica 'servicos/subscricao.publica.bicep' = {
  name: 'subscricao-publica'
  params: {
    namespaceName: namespaceName
    lockDuration: lockDuration
  }
}

// O que cada squad assina.
module subscricaoAssina 'servicos/subscricao.assina.bicep' = {
  name: 'subscricao-assina'
  params: {
    namespaceName: namespaceName
    lockDuration: lockDuration
    topicoPropostaRecebida: propostasPublica.outputs.topicoPropostaRecebida
    filaDeEntrada: subscricaoPublica.outputs.filaDeEntrada
  }
}

module propostasAssina 'servicos/propostas.assina.bicep' = {
  name: 'propostas-assina'
  params: {
    namespaceName: namespaceName
    lockDuration: lockDuration
    topicoApoliceEmitida: subscricaoPublica.outputs.topicoApoliceEmitida
    topicoPropostaRecusada: subscricaoPublica.outputs.topicoPropostaRecusada
  }
}

module cobrancaAssina 'servicos/cobranca.assina.bicep' = {
  name: 'cobranca-assina'
  params: {
    namespaceName: namespaceName
    lockDuration: lockDuration
    topicoApoliceEmitida: subscricaoPublica.outputs.topicoApoliceEmitida
  }
}

module notificacaoAssina 'servicos/notificacao.assina.bicep' = {
  name: 'notificacao-assina'
  params: {
    namespaceName: namespaceName
    lockDuration: lockDuration
    topicoApoliceEmitida: subscricaoPublica.outputs.topicoApoliceEmitida
    topicoPropostaRecusada: subscricaoPublica.outputs.topicoPropostaRecusada
  }
}

module resseguroAssina 'servicos/resseguro.assina.bicep' = {
  name: 'resseguro-assina'
  params: {
    namespaceName: namespaceName
    lockDuration: lockDuration
    topicoApoliceEmitida: subscricaoPublica.outputs.topicoApoliceEmitida
  }
}

// Mesmos outputs de antes da divisão: o up.ps1 não muda.
output fullyQualifiedNamespace string = plataforma.outputs.fullyQualifiedNamespace
output namespaceName string = namespaceName
output appInsightsConnectionString string = plataforma.outputs.appInsightsConnectionString
