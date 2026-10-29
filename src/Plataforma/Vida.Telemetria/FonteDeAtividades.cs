using System.Diagnostics;

namespace Vida.Telemetria;

/// <summary>
/// ActivitySource dos spans de negócio (AnalisarProposta, AplicarEndosso, PublicarOutbox).
/// Os spans de HTTP, SQL e Service Bus já vêm das instrumentações da distro; estes contam o que o código faz entre eles.
/// </summary>
public static class FonteDeAtividades
{
    /// <summary>Nome registrado no TracerProvider (ExtensoesDeTelemetria).</summary>
    public const string Nome = "Vida";

    /// <summary>
    /// Sem nenhum listener (telemetria desligada), StartActivity devolve null: o custo é zero.
    /// Por isso o uso é sempre "using var atividade = Fonte.StartActivity(...)" com "atividade?.SetTag".
    /// </summary>
    public static readonly ActivitySource Fonte = new(Nome);
}
