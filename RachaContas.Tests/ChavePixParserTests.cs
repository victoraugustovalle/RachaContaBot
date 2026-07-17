using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class ChavePixParserTests
{
    [Theory]
    [InlineData("telefone 11999998888", "+5511999998888")]
    [InlineData("telefone (11) 99999-8888", "+5511999998888")]
    [InlineData("telefone +55 11 99999-8888", "+5511999998888")]
    [InlineData("telefone 5511999998888", "+5511999998888")]
    [InlineData("celular 1133334444", "+551133334444")]
    public void TentarExtrair_Telefone_NormalizaComCodigoDoPaisSemPontuacao(string entrada, string esperado)
    {
        var conseguiu = ChavePixParser.TentarExtrair(entrada, out var chave, out _);

        Assert.True(conseguiu);
        Assert.Equal(esperado, chave);
    }

    [Theory]
    [InlineData("cpf 123.456.789-00", "12345678900")]
    [InlineData("cpf 12345678900", "12345678900")]
    public void TentarExtrair_Cpf_NormalizaSemPontuacao(string entrada, string esperado)
    {
        var conseguiu = ChavePixParser.TentarExtrair(entrada, out var chave, out _);

        Assert.True(conseguiu);
        Assert.Equal(esperado, chave);
    }

    [Fact]
    public void TentarExtrair_Email_MantemEMinusculiza()
    {
        var conseguiu = ChavePixParser.TentarExtrair("email Fulano@Exemplo.com", out var chave, out _);

        Assert.True(conseguiu);
        Assert.Equal("fulano@exemplo.com", chave);
    }

    [Fact]
    public void TentarExtrair_ChaveAleatoria_MantemOValor()
    {
        var conseguiu = ChavePixParser.TentarExtrair("aleatoria 123E4567-E89B-12D3-A456-426614174000", out var chave, out _);

        Assert.True(conseguiu);
        Assert.Equal("123e4567-e89b-12d3-a456-426614174000", chave);
    }

    [Fact]
    public void TentarExtrair_SemTipo_Rejeita()
    {
        var conseguiu = ChavePixParser.TentarExtrair("11999998888", out _, out var erro);

        Assert.False(conseguiu);
        Assert.Contains("formato", erro);
    }

    [Fact]
    public void TentarExtrair_TipoDesconhecido_Rejeita()
    {
        var conseguiu = ChavePixParser.TentarExtrair("pombocorreio 11999998888", out _, out var erro);

        Assert.False(conseguiu);
        Assert.Contains("pombocorreio", erro);
    }

    [Theory]
    [InlineData("telefone 123")]
    [InlineData("cpf 123")]
    public void TentarExtrair_QuantidadeDeDigitosErrada_Rejeita(string entrada)
    {
        var conseguiu = ChavePixParser.TentarExtrair(entrada, out _, out var erro);

        Assert.False(conseguiu);
        Assert.NotEmpty(erro);
    }
}
