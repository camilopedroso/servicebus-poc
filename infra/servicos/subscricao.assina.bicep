// Squad de Subscrição, o que ela ASSINA: o tópico de Propostas, encaminhando para a sua caixa de entrada.

import { propriedadesDeAssinatura } from 'padroes.bicep'

@description('Namespace criado por plataforma.bicep.')
param namespaceName string

@description('lockDuration da assinatura (ISO 8601, máx. PT5M).')
param lockDuration string

@description('Tópico publicado por propostas.publica.bicep.')
param topicoPropostaRecebida string

@description('Fila de entrada criada por subscricao.publica.bicep (destino do forwardTo).')
param filaDeEntrada string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: namespaceName
}

resource propostaRecebida 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' existing = {
  parent: namespace
  name: topicoPropostaRecebida
}

// Sem filtro ($Default) e com auto-forward para a caixa de entrada.
// O broker move cada mensagem, de forma transacional, sem código; não dá para abrir receiver aqui (nem peek).
// Se o encaminhamento falhar (destino cheio ou desabilitado), a mensagem fica na transfer DLQ desta assinatura
// (proposta-recebida/Subscriptions/subscricao/$Transfer/$DeadLetterQueue), não na DLQ da fila.
resource subscricao 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: propostaRecebida
  name: 'subscricao'
  properties: union(propriedadesDeAssinatura(lockDuration), {
    forwardTo: filaDeEntrada
  })
}
