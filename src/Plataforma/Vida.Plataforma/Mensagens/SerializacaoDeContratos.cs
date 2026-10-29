using System.Text.Json;

namespace Vida.Plataforma.Mensagens;

/// <summary>
/// Opções de JSON para ler contratos recebidos do broker.
/// Por padrão o System.Text.Json ignora campo desconhecido e deixa no valor padrão o que falta:
/// um corpo "{}" vira uma proposta com idade 0 e capital 0, e seria aprovada. Com estas opções,
/// campo obrigatório ausente ou nulo gera JsonException, e a mensagem vai para a DLQ como PayloadInvalido.
/// </summary>
public static class SerializacaoDeContratos
{
    public static readonly JsonSerializerOptions Leitura = new()
    {
        // Parâmetro do construtor do record sem valor padrão = obrigatório no JSON.
        RespectRequiredConstructorParameters = true,
        // string (não string?) não aceita null.
        RespectNullableAnnotations = true,
    };
}
