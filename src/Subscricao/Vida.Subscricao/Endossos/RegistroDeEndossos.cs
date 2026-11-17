using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vida.Subscricao.Contratos.Comandos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Dados;
using Vida.Subscricao.Outbox;

namespace Vida.Subscricao.Endossos;

/// <summary>
/// Grava os endossos no banco da Subscrição. Dois momentos:
/// - pedido (API): gera a próxima versão da apólice e grava o endosso com o comando EndossoSolicitado no outbox,
///   numa transação. Quem envia para a fila endossos é o DespachanteDoOutbox.
/// - aplicação (ProcessadorDeEndossos): avança a versão da apólice e marca o endosso como aplicado, de forma
///   idempotente (uma reentrega da mesma versão não muda nada).
/// Separado do controller e do processor para os testes de integração rodarem sem HTTP nem Service Bus.
/// </summary>
/// <param name="banco">DbContext do escopo (requisição ou mensagem).</param>
public sealed class RegistroDeEndossos(BancoDaSubscricao banco)
{
    // Violação de chave primária (2627) ou de índice único (2601) no SQL Server.
    private static readonly int[] ErrosDeChaveDuplicada = [2627, 2601];

    // Dois pedidos ao mesmo tempo na mesma apólice calculam a mesma versão; a chave primária barra o segundo,
    // que recalcula. Mais de 3 colisões seguidas na mesma apólice seria anormal: aí a exceção sobe.
    private const int TentativasDeGravacao = 3;

    /// <summary>
    /// Registra um pedido de endosso com a próxima versão da apólice.
    /// </summary>
    /// <param name="apoliceId">Apólice a alterar.</param>
    /// <param name="tipo">Tipo de alteração, já validado.</param>
    /// <param name="chave">Idempotency-Key do POST, ou null.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Registrado, repetido (mesma chave) ou apólice inexistente.</returns>
    public async Task<PedidoDeEndosso> SolicitarAsync(
        Guid apoliceId, string tipo, string? chave, CancellationToken cancellationToken)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            if (chave is not null)
            {
                var existente = await BuscarPorChaveAsync(apoliceId, chave, cancellationToken);
                if (existente is not null)
                {
                    return new PedidoDeEndosso(SituacaoDoPedidoDeEndosso.Repetido, existente);
                }
            }

            var apoliceExiste = await banco.Apolices.AnyAsync(a => a.ApoliceId == apoliceId, cancellationToken);
            if (!apoliceExiste)
            {
                return new PedidoDeEndosso(SituacaoDoPedidoDeEndosso.ApoliceInexistente, null);
            }

            var daMesmaApolice = banco.Endossos.Where(e => e.ApoliceId == apoliceId);
            var ultimaVersao = await daMesmaApolice.MaxAsync(e => (int?)e.Versao, cancellationToken) ?? 0;

            var endosso = new EndossoRegistrado
            {
                ApoliceId = apoliceId,
                Versao = ultimaVersao + 1,
                Tipo = tipo,
                SolicitadoEm = DateTimeOffset.UtcNow,
                ChaveDeIdempotencia = chave,
            };
            banco.Endossos.Add(endosso);

            var comando = new EndossoSolicitado(endosso.ApoliceId, endosso.Versao, endosso.Tipo, endosso.SolicitadoEm);
            var mensagem = MensagensEndosso.Criar(comando);
            var linhaDoOutbox = MensagemDeSaida.De(MensagensEndosso.Fila, mensagem);
            banco.OutboxMensagens.Add(linhaDoOutbox);

            try
            {
                // Um SaveChanges = uma transação: endosso e comando no outbox juntos.
                await banco.SaveChangesAsync(cancellationToken);
                return new PedidoDeEndosso(SituacaoDoPedidoDeEndosso.Registrado, endosso);
            }
            catch (DbUpdateException ex) when (
                tentativa < TentativasDeGravacao
                && ex.InnerException is SqlException sql
                && ErrosDeChaveDuplicada.Contains(sql.Number))
            {
                // Outro pedido levou a versão (ou a mesma chave) entre a leitura e o insert: descarta e recomeça.
                banco.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Aplica a versão na apólice: só avança se a apólice estiver exatamente na versão anterior.
    /// </summary>
    /// <param name="apoliceId">Apólice do endosso.</param>
    /// <param name="versao">Versão a aplicar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True se aplicou agora; false se a apólice não estava na versão anterior (reentrega de versão já aplicada).</returns>
    public async Task<bool> AplicarAsync(Guid apoliceId, int versao, CancellationToken cancellationToken)
    {
        var versaoAnterior = versao - 1;
        var agora = DateTimeOffset.UtcNow;

        // Dois UPDATEs, uma transação: a apólice e o endosso mudam juntos.
        await using var transacao = await banco.Database.BeginTransactionAsync(cancellationToken);

        var apolice = banco.Apolices.Where(a => a.ApoliceId == apoliceId && a.VersaoDosEndossos == versaoAnterior);
        var apolicesAlteradas = await apolice.ExecuteUpdateAsync(
            colunas => colunas.SetProperty(a => a.VersaoDosEndossos, versao),
            cancellationToken);

        var endosso = banco.Endossos.Where(e => e.ApoliceId == apoliceId && e.Versao == versao && e.AplicadoEm == null);
        await endosso.ExecuteUpdateAsync(
            colunas => colunas.SetProperty(e => e.AplicadoEm, agora),
            cancellationToken);

        await transacao.CommitAsync(cancellationToken);
        return apolicesAlteradas == 1;
    }

    private Task<EndossoRegistrado?> BuscarPorChaveAsync(Guid apoliceId, string chave, CancellationToken cancellationToken)
    {
        var somenteLeitura = banco.Endossos.AsNoTracking();
        return somenteLeitura.FirstOrDefaultAsync(
            e => e.ApoliceId == apoliceId && e.ChaveDeIdempotencia == chave, cancellationToken);
    }
}
