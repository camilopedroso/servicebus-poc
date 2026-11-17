using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;

namespace Vida.Operacao.Controllers;

/// <summary>
/// GET /namespace: o namespace do Service Bus em uso (confere para onde o serviço aponta).
/// </summary>
/// <param name="client">O ServiceBusClient singleton, que sabe a que namespace está ligado.</param>
[ApiController]
[Route("namespace")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class NamespaceController(ServiceBusClient client) : ControllerBase
{
    [HttpGet]
    public object Obter() => new { Namespace = client.FullyQualifiedNamespace };
}
