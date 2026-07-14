using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class CambioServiceTests
{
    [Fact]
    public void ExtraiTaxaBRL_DeJsonValido()
    {
        const string json = """{"amount":1.0,"base":"USD","date":"2026-07-14","rates":{"BRL":5.1233}}""";

        var taxa = CambioService.ExtrairTaxaBRL(json);

        Assert.Equal(5.1233m, taxa);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"rates\":{}}")]
    [InlineData("{\"base\":\"USD\"}")]
    public void JsonInvalidoOuSemBRL_RetornaNull(string json)
    {
        var taxa = CambioService.ExtrairTaxaBRL(json);

        Assert.Null(taxa);
    }
}
