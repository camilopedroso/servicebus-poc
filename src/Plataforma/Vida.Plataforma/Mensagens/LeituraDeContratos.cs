using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Vida.Plataforma.Mensagens;

/// <summary>
/// Lê o corpo de uma mensagem recebida como o contrato esperado, com as regras estritas de SerializacaoDeContratos.
/// </summary>
public static class LeituraDeContratos
{
    /// <summary>
    /// Lê o corpo como o contrato. Corpo ilegível volta null, e o chamador manda para a DLQ (PayloadInvalido):
    /// sem o catch, a exceção faria o processor abandonar e gastar as entregas à toa, já que repetir dá o mesmo erro.
    /// </summary>
    /// <typeparam name="T">Contrato esperado.</typeparam>
    /// <param name="mensagem">Mensagem recebida.</param>
    /// <returns>O contrato, ou null se o corpo não for um T válido.</returns>
    public static T? Ler<T>(ServiceBusReceivedMessage mensagem) where T : class
    {
        try
        {
            return mensagem.Body.ToObjectFromJson<T>(SerializacaoDeContratos.Leitura);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
