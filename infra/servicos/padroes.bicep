// Padrões da squad de Plataforma, importados pelos módulos das outras squads:
//   import { propriedadesDeAssinatura } from 'padroes.bicep'
// Cada squad continua dona das suas assinaturas; o padrão só evita que cada uma reinvente lock, retry e DLQ.

// Cada assinatura é uma "fila virtual" com cópia própria das mensagens que passam no filtro,
// com lock, maxDeliveryCount e DLQ independentes (≈ consumer group do Kafka / fila ligada a um exchange).
@export()
@description('Propriedades comuns de uma assinatura. lockDuration vem do up.ps1 (PT5M com -ModoDebug).')
func propriedadesDeAssinatura(lockDuration string) object => {
  lockDuration: lockDuration
  maxDeliveryCount: 5
  deadLetteringOnMessageExpiration: true
  // Mensagem que dá erro ao avaliar o filtro (ex.: tipo errado) vai para a DLQ em vez de sumir.
  deadLetteringOnFilterEvaluationExceptions: true
}
