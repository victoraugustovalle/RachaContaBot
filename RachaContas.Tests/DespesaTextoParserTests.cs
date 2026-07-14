using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class DespesaTextoParserTests
{
    private static string[] Partes(string texto) => texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void FormatoOficial_ValorPrimeiro_ContinuaFuncionando()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("50 uber pro aeroporto"), 0, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(50m, valor);
        Assert.Null(moeda);
        Assert.Equal("uber pro aeroporto", descricao);
    }

    [Fact]
    public void FormatoInvertido_DescricaoPrimeiro_NumeroPuro()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber 5"), 0, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(5m, valor);
        Assert.Null(moeda);
        Assert.Equal("uber", descricao);
    }

    [Fact]
    public void FormatoInvertido_DescricaoComVariasPalavras()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber pro aeroporto 45,50"), 0, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(45.50m, valor);
        Assert.Equal("uber pro aeroporto", descricao);
    }

    [Fact]
    public void FormatoInvertido_ComRComColado()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber R$5"), 0, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(5m, valor);
        Assert.Null(moeda);
        Assert.Equal("uber", descricao);
    }

    [Fact]
    public void FormatoInvertido_ComSimboloSeparadoNoFinal_NaoQuebraOSimbolo()
    {
        // Caso que quebraria se testássemos só o último token isolado primeiro:
        // "US$" ficaria preso na descrição e só "50" viraria valor (em BRL, errado).
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber pro aeroporto US$ 50"), 0, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(50m, valor);
        Assert.Equal("USD", moeda);
        Assert.Equal("uber pro aeroporto", descricao);
    }

    [Fact]
    public void FormatoInvertido_ComCodigoDeMoedaNoFinal()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber pro aeroporto 50 USD"), 0, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(50m, valor);
        Assert.Equal("USD", moeda);
        Assert.Equal("uber pro aeroporto", descricao);
    }

    [Fact]
    public void SemValorAlgumNoTexto_Rejeita()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber pro aeroporto"), 0, out _, out _, out _);

        Assert.False(conseguiu);
    }

    [Fact]
    public void NumeroNoMeioDaDescricao_NaoConfundeComValorInvertido()
    {
        // "5" no meio (não no fim) não deve ser lido como valor invertido — falta um valor de verdade.
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("uber 5 estrelas"), 0, out _, out _, out _);

        Assert.False(conseguiu);
    }

    [Fact]
    public void ComPrefixoDeComando_IndiceInicialUm()
    {
        var conseguiu = DespesaTextoParser.TentarExtrair(Partes("/despesa uber 45,50"), 1, out var valor, out var moeda, out var descricao);

        Assert.True(conseguiu);
        Assert.Equal(45.50m, valor);
        Assert.Equal("uber", descricao);
    }
}
