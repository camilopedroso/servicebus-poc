// Squad de Propostas, o que ela PUBLICA: o tópico do evento "proposta recebida".
// Quem publica cria o tópico; quem consome cria a sua assinatura (nos módulos *.assina.bicep de cada squad).

@description('Namespace criado por plataforma.bicep.')
param namespaceName string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: namespaceName
}

// Propostas publica o FATO (evento), sem saber quem reage.
// Antes era a fila "propostas", um COMANDO endereçado à Subscrição: um interessado novo (antifraude, CRM)
// exigia mudar a API. Agora ele só cria a sua assinatura.
resource propostaRecebida 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: 'proposta-recebida'
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

// Os nomes saem como output: quem assina recebe o nome por parâmetro, e isso faz o deploy criar o tópico antes.
output topicoPropostaRecebida string = propostaRecebida.name
