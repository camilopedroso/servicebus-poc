using Azure.Messaging.ServiceBus.Administration;
using Vida.Operacao.Contratos;

namespace Vida.Operacao.Ferramentas;

/// <summary>
/// Descobre no broker as filas, os tópicos e as assinaturas que existem agora. A ferramenta de operação não tem
/// lista fixa: uma squad nova cria a sua assinatura no Bicep dela, e ela aparece aqui sem mudar este código.
/// </summary>
/// <param name="admin">Administration client: operações de gerenciamento pelo data plane (com o RBAC Data Owner).</param>
public sealed class DescobertaDeEntidades(ServiceBusAdministrationClient admin)
{
    /// <summary>
    /// Todas as filas do namespace, com as contagens de agora.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Filas em ordem alfabética.</returns>
    public async Task<FilaDescoberta[]> ListarFilasAsync(CancellationToken cancellationToken)
    {
        var filas = new List<FilaDescoberta>();
        var propriedadesDasFilas = admin.GetQueuesAsync(cancellationToken);
        await foreach (var fila in propriedadesDasFilas)
        {
            var execucao = await admin.GetQueueRuntimePropertiesAsync(fila.Name, cancellationToken);
            var contagens = execucao.Value;
            var descoberta = new FilaDescoberta(
                fila.Name, fila.RequiresSession, contagens.ActiveMessageCount, contagens.DeadLetterMessageCount,
                contagens.ScheduledMessageCount);
            filas.Add(descoberta);
        }
        return [.. filas.OrderBy(f => f.Nome)];
    }

    /// <summary>
    /// Todas as assinaturas de todos os tópicos, com as contagens de agora.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Assinaturas em ordem de tópico e nome.</returns>
    public async Task<AssinaturaDescoberta[]> ListarAssinaturasAsync(CancellationToken cancellationToken)
    {
        var assinaturas = new List<AssinaturaDescoberta>();
        var topicos = admin.GetTopicsAsync(cancellationToken);
        await foreach (var topico in topicos)
        {
            var propriedadesDasAssinaturas = admin.GetSubscriptionsAsync(topico.Name, cancellationToken);
            await foreach (var assinatura in propriedadesDasAssinaturas)
            {
                var execucao = await admin.GetSubscriptionRuntimePropertiesAsync(
                    topico.Name, assinatura.SubscriptionName, cancellationToken);
                var contagens = execucao.Value;
                // O broker devolve o forwardTo como URL completa (sb://<namespace>/<fila>); para o painel basta o nome.
                var encaminhaPara = NomeDoDestino(assinatura.ForwardTo);
                var descoberta = new AssinaturaDescoberta(
                    topico.Name, assinatura.SubscriptionName, encaminhaPara,
                    contagens.ActiveMessageCount, contagens.DeadLetterMessageCount);
                assinaturas.Add(descoberta);
            }
        }
        return [.. assinaturas.OrderBy(a => a.Topico).ThenBy(a => a.Nome)];
    }

    private static string? NomeDoDestino(string? forwardTo)
    {
        if (string.IsNullOrEmpty(forwardTo))
        {
            return null;
        }
        var semBarraNoFim = forwardTo.TrimEnd('/');
        var ultimaBarra = semBarraNoFim.LastIndexOf('/');
        return ultimaBarra < 0 ? semBarraNoFim : semBarraNoFim[(ultimaBarra + 1)..];
    }
}
