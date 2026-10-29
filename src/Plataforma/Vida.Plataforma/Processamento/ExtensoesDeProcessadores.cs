using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vida.Plataforma.Conexao;

namespace Vida.Plataforma.Processamento;

/// <summary>
/// Liga/desliga dos processors: registra o gerenciador, os controllers de /processadores e /assinaturas e cada processador.
/// </summary>
public static class ExtensoesDeProcessadores
{
    /// <summary>
    /// Registra o GerenciadorDeProcessadores (hosted service), o ProcessadoresController e o AssinaturasController.
    /// O app precisa chamar app.MapControllers().
    /// </summary>
    /// <param name="builder">Builder do app.</param>
    /// <returns>O mesmo builder, para encadear.</returns>
    public static WebApplicationBuilder AddProcessadoresControlaveis(this WebApplicationBuilder builder)
    {
        // Singleton e hosted service na mesma instância: o controller usa a que o host ligou.
        builder.Services.AddSingleton<GerenciadorDeProcessadores>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GerenciadorDeProcessadores>());
        // Só para ler contagens e regras das assinaturas (GET /assinaturas). Criado na primeira requisição:
        // num app sem assinaturas (só filas), nunca chega a ser criado.
        builder.Services.TryAddSingleton(sp =>
        {
            var configuracao = sp.GetRequiredService<IConfiguration>();
            var fqdn = ServiceBusClientFactory.ObterNamespace(configuracao);
            var credencial = new DefaultAzureCredential();
            return new ServiceBusAdministrationClient(fqdn, credencial);
        });
        // O controller mora nesta biblioteca, não no app: o application part explícito garante que o MVC o encontre.
        var mvc = builder.Services.AddControllers();
        var estaBiblioteca = typeof(ProcessadoresController).Assembly;
        mvc.AddApplicationPart(estaBiblioteca);
        return builder;
    }

    /// <summary>
    /// Registra um processador para o GerenciadorDeProcessadores. A fábrica roda uma vez, quando o gerenciador é criado.
    /// </summary>
    /// <param name="services">Serviços do app.</param>
    /// <param name="criar">Cria o processor (com os handlers) e o embrulha num ProcessadorControlavel.</param>
    /// <returns>Os mesmos serviços, para encadear.</returns>
    public static IServiceCollection AddProcessadorControlavel(
        this IServiceCollection services, Func<IServiceProvider, ProcessadorControlavel> criar) =>
        // Vários registros do mesmo tipo: o gerenciador recebe todos como IEnumerable<ProcessadorControlavel>.
        services.AddSingleton(criar);
}
