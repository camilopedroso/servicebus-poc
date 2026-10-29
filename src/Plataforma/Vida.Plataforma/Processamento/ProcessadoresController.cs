using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Vida.Plataforma.Processamento;

/// <summary>
/// Liga e desliga os processors do app em tempo de execução. Registrado por AddProcessadoresControlaveis()
/// nos apps que consomem (o app precisa chamar app.MapControllers()).
/// </summary>
/// <param name="gerenciador">Dono dos processadores do app.</param>
[ApiController]
[Route("processadores")]
public sealed class ProcessadoresController(GerenciadorDeProcessadores gerenciador) : ControllerBase
{
    /// <summary>
    /// GET /processadores: nome, entidade e se está ligado, para cada processador do app.
    /// </summary>
    [HttpGet]
    public EstadoDoProcessador[] Listar()
    {
        var estados = new List<EstadoDoProcessador>();
        foreach (var processador in gerenciador.Processadores)
        {
            var estado = new EstadoDoProcessador(processador.Nome, processador.Entidade, processador.Ativo);
            estados.Add(estado);
        }
        return [.. estados];
    }

    /// <summary>
    /// POST /processadores/{nome}/iniciar: volta a buscar mensagens, inclusive as que esperaram enquanto estava parado.
    /// </summary>
    [HttpPost("{nome}/iniciar")]
    public async Task<IActionResult> IniciarAsync(string nome, CancellationToken cancellationToken)
    {
        var processador = gerenciador.Encontrar(nome);
        if (processador is null)
        {
            return ProcessadorDesconhecido(nome);
        }
        await gerenciador.IniciarAsync(processador, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// POST /processadores/{nome}/parar: para de buscar mensagens; o processo continua no ar e elas ficam na entidade.
    /// </summary>
    [HttpPost("{nome}/parar")]
    public async Task<IActionResult> PararAsync(string nome, CancellationToken cancellationToken)
    {
        var processador = gerenciador.Encontrar(nome);
        if (processador is null)
        {
            return ProcessadorDesconhecido(nome);
        }
        await gerenciador.PararAsync(processador, cancellationToken);
        return NoContent();
    }

    private ObjectResult ProcessadorDesconhecido(string nome)
    {
        var nomes = new List<string>();
        foreach (var processador in gerenciador.Processadores)
        {
            nomes.Add(processador.Nome);
        }
        var validos = string.Join(", ", nomes);
        return Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Processador desconhecido",
            detail: $"'{nome}' não existe neste app. Válidos: {validos}.");
    }
}
