namespace Vida.Subscricao.Contratos.Mensagens;

/// <summary>
/// Application properties das mensagens da Subscrição (metadados fora do corpo, ≈ headers no Kafka/RabbitMQ).
/// Filtros de assinatura só enxergam propriedades, nunca o corpo: o que for filtrável tem que estar aqui.
/// </summary>
public static class PropriedadesDeRoteamento
{
    public const string Plano = "plano";

    /// <summary>Capital em reais inteiros (long): o filtro SQL compara números, e decimal não é bem suportado.</summary>
    public const string CapitalSegurado = "capitalSegurado";

    /// <summary>
    /// Bool com a decisão da PoliticaDeRevisao. Os filtros de cobranca (= FALSE) e alto-valor (= TRUE) usam esta
    /// propriedade, e não o capital, para que o limite exista só no código de domínio.
    /// </summary>
    public const string RequerRevisao = "requerRevisao";

    /// <summary>Definida pela action da regra da assinatura alto-valor (não é enviada pelo publisher).</summary>
    public const string Prioridade = "prioridade";

    /// <summary>Versão do endosso dentro da apólice (1, 2, 3...). Não roteia: é só para leitura no peek.</summary>
    public const string Versao = "versao";
}
