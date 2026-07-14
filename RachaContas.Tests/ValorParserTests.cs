using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class ValorParserTests
{
    [Theory]
    [InlineData("34,22", 34.22)]      // BR: vírgula decimal
    [InlineData("33.33", 33.33)]      // US: ponto decimal
    [InlineData("30", 30)]            // sem separador
    [InlineData("45,50", 45.50)]
    [InlineData("1.450,90", 1450.90)] // BR com milhar: ponto = milhar, vírgula = decimal
    [InlineData("1,450.90", 1450.90)] // US com milhar: vírgula = milhar, ponto = decimal
    public void ParseiaValorCorretamente(string texto, double esperado)
    {
        var conseguiu = ValorParser.TentarParsear(texto, out var valor);

        Assert.True(conseguiu);
        Assert.Equal((decimal)esperado, valor);
    }

    [Theory]
    [InlineData("pizza")]
    [InlineData("")]
    [InlineData("R$")]
    public void RejeitaTextoInvalido(string texto)
    {
        var conseguiu = ValorParser.TentarParsear(texto, out _);

        Assert.False(conseguiu);
    }
}
