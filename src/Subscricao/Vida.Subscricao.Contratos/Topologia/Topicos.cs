using Vida.Subscricao.Contratos.Eventos;

namespace Vida.Subscricao.Contratos.Topologia;

public static class Topicos
{
    public const string ApoliceEmitida = "apolice-emitida";

    /// <summary>Recusas por regra de negócio, publicadas pelo consumer ao concluir a proposta.</summary>
    public const string PropostaRecusada = "proposta-recusada";
}
