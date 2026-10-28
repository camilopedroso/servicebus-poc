using Vida.Mensageria.Dominio;

namespace Vida.Producer.Contratos.Demonstracao;

/// <summary>
/// Corpo de POST /demo/propostas. Campos nulos são sorteados.
/// </summary>
/// <param name="Idade">
///     Idade do proponente. Acima de 80 o consumer recusa e publica 
///     PropostaRecusada.
/// </param>
/// <param name="CapitalSegurado">
///     Capital segurado em reais. 
///     Acima de 1.000.000 a apólice cai na assinatura alto-valor.
/// </param>
/// <param name="Plano">Individual, Familiar ou Empresarial.</param>
/// <param name="SimularFalha">
///     Se true, o consumer abandona até maxDeliveryCount e o broker manda 
///     para a DLQ.
/// </param>
/// <param name="RetryComBackoff">
///     Com SimularFalha, o consumer agenda novas tentativas com espera 
///     crescente em vez de abandonar.
/// </param>
public sealed record EnvioDeProposta(
    int? Idade,
    decimal? CapitalSegurado,
    string? Plano,
    bool SimularFalha,
    bool RetryComBackoff = false)
{
    /// <summary>Erros de validação por campo; vazio se o corpo for válido.</summary>
    public Dictionary<string, string[]> Validar()
    {
        var erros = new Dictionary<string, string[]>();
        if (Idade is < 0 or > 130)
        {
            erros[nameof(Idade)] = ["Informe uma idade entre 0 e 130."];
        }
        if (CapitalSegurado is <= 0)
        {
            erros[nameof(CapitalSegurado)] = ["O capital segurado precisa ser maior que zero."];
        }
        if (Plano is not null && Planos.Interpretar(Plano) is null)
        {
            erros[nameof(Plano)] = [$"Planos válidos: {string.Join(", ", Planos.Todos)}."];
        }
        return erros;
    }
}
