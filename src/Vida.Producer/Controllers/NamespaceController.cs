using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;

namespace Vida.Producer.Controllers;

/// <summary>
/// GET /namespace: o namespace do Service Bus em uso (o painel mostra no cabeçalho).
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
