namespace Vida.Mensageria.Processamento;

/// <summary>
/// Estado de um processador como o painel mostra no liga/desliga.
/// </summary>
/// <param name="Nome">Nome do processador (o mesmo da rota).</param>
/// <param name="Entidade">Fila ou assinatura que ele lê.</param>
/// <param name="Ativo">Se está buscando mensagens.</param>
public sealed record EstadoDoProcessador(string Nome, string Entidade, bool Ativo);
