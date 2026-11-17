namespace Vida.Plataforma.Processamento;

/// <summary>
/// Estado de um processador como GET /processadores devolve.
/// </summary>
/// <param name="Nome">Nome do processador (o mesmo da rota).</param>
/// <param name="Entidade">Fila ou assinatura que ele lê.</param>
/// <param name="Ativo">Se está buscando mensagens.</param>
public sealed record EstadoDoProcessador(string Nome, string Entidade, bool Ativo);
