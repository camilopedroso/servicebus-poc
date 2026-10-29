namespace Vida.Subscricao.Contratos.Topologia;

/// <summary>Nomes das entidades criadas em infra/main.bicep.</summary>
public static class Filas
{
    /// <summary>
    /// Caixa de entrada da Subscrição: recebe as propostas pela assinatura "subscricao" do tópico "proposta-recebida"
    /// (forwardTo, sem código). É para cá, e não para o tópico, que vão a retentativa agendada e o reprocessamento
    /// da DLQ: no tópico, a cópia chegaria a todas as assinaturas.
    /// </summary>
    public const string SubscricaoEntrada = "subscricao-entrada";

    /// <summary>Fase 3. Com sessions (SessionId = ApoliceId) e detecção de duplicatas.</summary>
    public const string Endossos = "endossos";
}
