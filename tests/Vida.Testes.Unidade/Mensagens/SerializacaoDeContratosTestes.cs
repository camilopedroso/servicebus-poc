using System.Text.Json;
using Vida.Mensageria.Contratos;
using Vida.Mensageria.Mensagens;

namespace Vida.Testes.Unidade.Mensagens;

/// <summary>
/// O bug que estes testes protegem: com as opções padrão, "{}" virava uma proposta com idade 0 e era aprovada.
/// </summary>
public class SerializacaoDeContratosTestes
{
    private static PropostaSeguroVida? Ler(string json)
    {
        var corpo = BinaryData.FromString(json);
        return corpo.ToObjectFromJson<PropostaSeguroVida>(SerializacaoDeContratos.Leitura);
    }

    [Theory]
    [InlineData("{}")]
    // camelCase: o contrato é serializado em PascalCase; sem a leitura estrita, todos os campos ficavam no padrão.
    [InlineData("""{"propostaId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","clienteId":"C","idade":30,"capitalSegurado":1,"plano":"X","criadaEm":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"PropostaId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","ClienteId":"C","Idade":"trinta","CapitalSegurado":1,"Plano":"X","CriadaEm":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"PropostaId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","ClienteId":"C","Idade":30,"CapitalSegurado":1,"Plano":null,"CriadaEm":"2026-01-01T00:00:00Z"}""")]
    public void Corpo_invalido_gera_JsonException(string json) =>
        Assert.Throws<JsonException>(() => Ler(json));

    [Fact]
    public void Corpo_valido_e_lido()
    {
        var original = new PropostaSeguroVida(Guid.NewGuid(), "CLI-1", 30, 100_000m, "Familiar", DateTimeOffset.UtcNow);

        var corpo = BinaryData.FromObjectAsJson(original);
        var json = corpo.ToString();

        var lida = Ler(json);

        Assert.Equal(original, lida);
    }
}
