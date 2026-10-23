namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Nomes de application properties (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).
/// Filtros de assinatura só enxergam propriedades, nunca o corpo: o que for filtrável tem que estar aqui.
/// </summary>
public static class Propriedades
{
    /// <summary>Quando true, o consumer simula uma falha transitória (para exercitar retry → DLQ).</summary>
    public const string SimularFalha = "simularFalha";

    public const string Plano = "plano";

    /// <summary>Capital em reais inteiros (long): o filtro SQL compara números, e decimal não é bem suportado.</summary>
    public const string CapitalSegurado = "capitalSegurado";

    /// <summary>Definida pela action da regra da assinatura alto-valor (não é enviada pelo publisher).</summary>
    public const string Prioridade = "prioridade";
}
