using Microsoft.AspNetCore.Mvc;

namespace Vida.Producer.Controllers;

/// <summary>
/// Liga o Validar() dos contratos ao ModelState do MVC.
/// </summary>
public static class ExtensoesDeValidacao
{
    /// <summary>
    /// 400 com ValidationProblemDetails, igual ao que o [ApiController] devolve para erro de binding
    /// (type, title e traceId vêm da ProblemDetailsFactory).
    /// </summary>
    /// <param name="controller">Controller da requisição.</param>
    /// <param name="erros">Erros por campo, como devolvidos por Validar().</param>
    /// <returns>Resultado 400.</returns>
    public static ActionResult ProblemaDeValidacao(this ControllerBase controller, Dictionary<string, string[]> erros)
    {
        foreach (var (campo, mensagens) in erros)
        {
            foreach (var mensagem in mensagens)
            {
                controller.ModelState.AddModelError(campo, mensagem);
            }
        }
        return controller.ValidationProblem(controller.ModelState);
    }
}
