// Squad de Resseguro, o que ela ASSINA: apólices que exigem revisão manual e resseguro.
// A assinatura pode existir antes do serviço: as mensagens esperam aqui (TTL 14 dias, depois DLQ) até alguém
// consumir. Quem publica não sabe nem precisa saber se há consumidor. Hoje quem lê é o Vida.Assinantes;
// na etapa 5 do plano de reestruturação ela fica sem serviço, de propósito.

import { propriedadesDeAssinatura } from 'padroes.bicep'

@description('Namespace criado por plataforma.bicep.')
param namespaceName string

@description('lockDuration da assinatura (ISO 8601, máx. PT5M).')
param lockDuration string

@description('Tópico publicado por subscricao.publica.bicep.')
param topicoApoliceEmitida string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: namespaceName
}

resource apoliceEmitida 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' existing = {
  parent: namespace
  name: topicoApoliceEmitida
}

resource altoValor 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'alto-valor'
  properties: propriedadesDeAssinatura(lockDuration)
}

// Regra com o filtro SQL e a action de prioridade alta.
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
