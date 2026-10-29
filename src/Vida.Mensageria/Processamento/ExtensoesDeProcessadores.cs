using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Vida.Mensageria.Processamento;

/// <summary>
/// Liga/desliga dos processors: registra o gerenciador, o controller de /processadores e cada processador.
/// </summary>
public static class ExtensoesDeProcessadores
{
    /// <summary>
    /// Registra o GerenciadorDeProcessadores (hosted service) e o ProcessadoresController. O app precisa chamar app.MapControllers().
    /// </summary>
    /// <param name="builder">Builder do app.</param>
    /// <returns>O mesmo builder, para encadear.</returns>
    public static WebApplicationBuilder AddProcessadoresControlaveis(this WebApplicationBuilder builder)
    {
        // Singleton e hosted service na mesma instância: o controller usa a que o host ligou.
        builder.Services.AddSingleton<GerenciadorDeProcessadores>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GerenciadorDeProcessadores>());
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
