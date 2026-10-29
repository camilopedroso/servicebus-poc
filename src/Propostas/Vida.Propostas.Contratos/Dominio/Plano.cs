using System.Text.Json.Serialization;

namespace Vida.Propostas.Contratos.Dominio;

/// <summary>
/// Plano de seguro de vida do portfólio.
/// O conversor no próprio tipo faz o JSON usar o nome ("Familiar") e não o número, em qualquer serialização:
/// corpo das mensagens, respostas da API e o contrato OpenAPI, que passa a listar os valores.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Plano>))]
public enum Plano
{
    Individual,
    Familiar,
    Empresarial,
}
