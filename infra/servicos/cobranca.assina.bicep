// Squad de Cobrança, o que ela ASSINA: apólices emitidas que podem ser cobradas já.

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

resource cobranca 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: apoliceEmitida
  name: 'cobranca'
  properties: propriedadesDeAssinatura(lockDuration)
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
