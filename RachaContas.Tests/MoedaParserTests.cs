using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class MoedaParserTests
{
    private static string[] Partes(string texto) => texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Theory]
    [InlineData("$50", 50, "USD", 1)]
    [InlineData("US$50", 50, "USD", 1)]
    [InlineData("U$50", 50, "USD", 1)]
    [InlineData("€30", 30, "EUR", 1)]
    [InlineData("£20", 20, "GBP", 1)]
    [InlineData("R$50", 50, null, 1)]
    public void SimboloColado_DetectaMoedaEValor(string token, double valorEsperado, string? moedaEsperada, int proximoIndiceEsperado)
    {
        var conseguiu = MoedaParser.TentarExtrair([token], 0, out var valor, out var moeda, out var proximoIndice);

        Assert.True(conseguiu);
        Assert.Equal((decimal)valorEsperado, valor);
        Assert.Equal(moedaEsperada, moeda);
        Assert.Equal(proximoIndiceEsperado, proximoIndice);
    }

    [Theory]
    [InlineData("US$ 50 uber", 50, "USD")]
    [InlineData("€ 30 pizza", 30, "EUR")]
    [InlineData("R$ 50 uber", 50, null)]
    public void SimboloSeparado_DetectaMoedaEValor(string texto, double valorEsperado, string? moedaEsperada)
    {
        var conseguiu = MoedaParser.TentarExtrair(Partes(texto), 0, out var valor, out var moeda, out var proximoIndice);

        Assert.True(conseguiu);
        Assert.Equal((decimal)valorEsperado, valor);
        Assert.Equal(moedaEsperada, moeda);
        Assert.Equal(2, proximoIndice); // consumiu símbolo + número, descrição começa no índice 2
    }

    [Fact]
    public void CodigoDepoisDoNumero_DetectaMoedaEstrangeira()
    {
        var conseguiu = MoedaParser.TentarExtrair(Partes("50 USD uber"), 0, out var valor, out var moeda, out var proximoIndice);

        Assert.True(conseguiu);
        Assert.Equal(50m, valor);
        Assert.Equal("USD", moeda);
        Assert.Equal(2, proximoIndice);
    }

    [Fact]
    public void SemSimboloNemCodigo_AssumeBRL()
    {
        var conseguiu = MoedaParser.TentarExtrair(Partes("30 uber"), 0, out var valor, out var moeda, out var proximoIndice);

        Assert.True(conseguiu);
        Assert.Equal(30m, valor);
        Assert.Null(moeda);
        Assert.Equal(1, proximoIndice);
    }

    [Fact]
    public void NumeroComVirgulaDecimal_AindaFuncionaComSimbolo()
    {
        var conseguiu = MoedaParser.TentarExtrair(["US$34,22"], 0, out var valor, out var moeda, out _);

        Assert.True(conseguiu);
        Assert.Equal(34.22m, valor);
        Assert.Equal("USD", moeda);
    }

    [Fact]
    public void TextoInvalido_Rejeita()
    {
        var conseguiu = MoedaParser.TentarExtrair(Partes("pizza terça"), 0, out _, out _, out _);

        Assert.False(conseguiu);
    }
}
