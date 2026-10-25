namespace Vida.Mensageria.Mensagens;

/// <summary>
/// Nomes de application properties (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).
/// Filtros de assinatura só enxergam propriedades, nunca o corpo: o que for filtrável tem que estar aqui.
/// </summary>
public static class Propriedades
{
    /// <summary>Quando true, o consumer simula uma falha transitória (para exercitar retry → DLQ).</summary>
    public const string SimularFalha = "simularFalha";

    /// <summary>
    /// Quando true junto com SimularFalha, o consumer não abandona: agenda uma cópia com espera crescente (fase 3).
    /// </summary>
    public const string RetryComBackoff = "retryComBackoff";

    /// <summary>
    /// Número da tentativa no retry com backoff. Cada cópia agendada é uma mensagem nova (DeliveryCount volta a 1),
    /// então a contagem precisa viajar na própria mensagem.
    /// </summary>
    public const string Tentativa = "tentativa";

    public const string Plano = "plano";

    /// <summary>Versão do endosso dentro da apólice (1, 2, 3...).</summary>
    public const string Versao = "versao";

    /// <summary>Capital em reais inteiros (long): o filtro SQL compara números, e decimal não é bem suportado.</summary>
    public const string CapitalSegurado = "capitalSegurado";

    /// <summary>
    /// Bool com a decisão da PoliticaDeRevisao. Os filtros de cobranca (= FALSE) e alto-valor (= TRUE) usam esta
    /// propriedade, e não o capital, para que o limite exista só no código de domínio.
    /// </summary>
    public const string RequerRevisao = "requerRevisao";

    /// <summary>Definida pela action da regra da assinatura alto-valor (não é enviada pelo publisher).</summary>
    public const string Prioridade = "prioridade";
}
