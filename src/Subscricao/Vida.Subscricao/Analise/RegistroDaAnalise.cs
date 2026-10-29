using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vida.Subscricao.Contratos.Eventos;
using Vida.Subscricao.Contratos.Mensagens;
using Vida.Subscricao.Dados;
using Vida.Subscricao.Inbox;
using Vida.Subscricao.Outbox;

namespace Vida.Subscricao.Analise;

/// <summary>
/// Grava o desfecho de uma análise numa transação só do SQL Server: a linha da inbox (MessageId), o dado de negócio
/// (apólice ou recusa) e a mensagem do outbox (o evento a publicar). Ou as três entram, ou nenhuma.
/// Depois do commit, o ProcessadorDePropostas conclui a mensagem; se cair entre os dois, a reentrega cai na inbox.
/// </summary>
/// <param name="banco">DbContext da mensagem (um escopo por mensagem).</param>
public sealed class RegistroDaAnalise(BancoDaSubscricao banco)
{
    // Violação de chave primária (2627) ou de índice único (2601) no SQL Server.
    private static readonly int[] ErrosDeChaveDuplicada = [2627, 2601];

    /// <summary>
    /// Registra uma proposta aprovada: apólice + evento ApoliceEmitida no outbox + MessageId na inbox.
    /// </summary>
    /// <param name="messageId">MessageId da mensagem recebida (a PropostaId).</param>
    /// <param name="apolice">Apólice emitida.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True se gravou; false se a mensagem já tinha sido processada (reentrega).</returns>
    public async Task<bool> RegistrarAprovacaoAsync(string messageId, ApoliceEmitida apolice, CancellationToken cancellationToken)
    {
        if (await JaProcessadaAsync(messageId, cancellationToken))
        {
            return false;
        }
        var linhaDaApolice = ApoliceRegistrada.De(apolice);
        banco.Apolices.Add(linhaDaApolice);
        var mensagem = MensagensApolice.Criar(apolice);
        var linhaDoOutbox = MensagemDeSaida.De(MensagensApolice.Topico, mensagem);
        banco.OutboxMensagens.Add(linhaDoOutbox);
        return await SalvarComInboxAsync(messageId, cancellationToken);
    }

    /// <summary>
    /// Registra uma proposta recusada por regra de negócio: recusa + evento PropostaRecusada no outbox + inbox.
    /// </summary>
    /// <param name="messageId">MessageId da mensagem recebida (a PropostaId).</param>
    /// <param name="recusa">Recusa decidida.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True se gravou; false se a mensagem já tinha sido processada (reentrega).</returns>
    public async Task<bool> RegistrarRecusaAsync(string messageId, PropostaRecusada recusa, CancellationToken cancellationToken)
    {
        if (await JaProcessadaAsync(messageId, cancellationToken))
        {
            return false;
        }
        var linhaDaRecusa = RecusaRegistrada.De(recusa);
        banco.Recusas.Add(linhaDaRecusa);
        var mensagem = MensagensRecusa.Criar(recusa);
        var linhaDoOutbox = MensagemDeSaida.De(MensagensRecusa.Topico, mensagem);
        banco.OutboxMensagens.Add(linhaDoOutbox);
        return await SalvarComInboxAsync(messageId, cancellationToken);
    }

    /// <summary>
    /// Consulta rápida para o caso comum (reentrega depois de um commit). Não basta sozinha: duas instâncias podem
    /// consultar ao mesmo tempo, e quem decide é a chave primária da inbox, no SaveChanges.
    /// </summary>
    private Task<bool> JaProcessadaAsync(string messageId, CancellationToken cancellationToken) =>
        banco.Inbox.AnyAsync(m => m.MessageId == messageId, cancellationToken);

    private async Task<bool> SalvarComInboxAsync(string messageId, CancellationToken cancellationToken)
    {
        var linhaDaInbox = new MensagemRecebida { MessageId = messageId, ProcessadaEm = DateTimeOffset.UtcNow };
        banco.Inbox.Add(linhaDaInbox);
        try
        {
            // Um SaveChanges = uma transação: inbox, negócio e outbox juntos.
            await banco.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && ErrosDeChaveDuplicada.Contains(sql.Number))
        {
            // Outra instância gravou o mesmo MessageId entre a consulta e o insert: nada entrou desta vez.
            return false;
        }
    }
}
