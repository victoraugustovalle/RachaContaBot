using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class DebtSimplifierTests
{
    [Fact]
    public void DuasPessoas_GeraUmaTransferencia()
    {
        var saldos = new[]
        {
            new SaldoParticipante(1, "Ana", 50m),
            new SaldoParticipante(2, "Bia", -50m),
        };

        var resultado = DebtSimplifier.Simplificar(saldos);

        var t = Assert.Single(resultado);
        Assert.Equal(2, t.DevedorId);
        Assert.Equal("Bia", t.De);
        Assert.Equal(1, t.CredorId);
        Assert.Equal("Ana", t.Para);
        Assert.Equal(50m, t.Valor);
    }

    [Fact]
    public void TresPessoas_UmDevedorDoisCredores_GeraDuasTransferencias()
    {
        var saldos = new[]
        {
            new SaldoParticipante(1, "Ana", -30m),
            new SaldoParticipante(2, "Bia", 10m),
            new SaldoParticipante(3, "Caio", 20m),
        };

        var resultado = DebtSimplifier.Simplificar(saldos);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, t => Assert.Equal("Ana", t.De));
        Assert.Equal(30m, resultado.Sum(t => t.Valor));
    }

    [Fact]
    public void CicloFechado_JaEstaQuitado_NaoGeraTransferencia()
    {
        // A pagou 10 a mais, B está no zero a zero, C deve 10 — mas o saldo líquido
        // de todo mundo já é zero (ninguém tem saldo residual), então nada a fazer.
        var saldos = new[]
        {
            new SaldoParticipante(1, "Ana", 0m),
            new SaldoParticipante(2, "Bia", 0m),
            new SaldoParticipante(3, "Caio", 0m),
        };

        var resultado = DebtSimplifier.Simplificar(saldos);

        Assert.Empty(resultado);
    }

    [Fact]
    public void DivisaoComDizimaPeriodica_NaoSobraResiduo()
    {
        // R$10 dividido por 3 — 3.3333...m por pessoa. Ana pagou tudo, então o
        // saldo dela é 10 menos a própria cota (ela não deve nada a si mesma).
        var cota = 10m / 3m;
        var saldoAna = 10m - cota;
        var saldos = new[]
        {
            new SaldoParticipante(1, "Ana", saldoAna),
            new SaldoParticipante(2, "Bia", -cota),
            new SaldoParticipante(3, "Caio", -cota),
        };

        var resultado = DebtSimplifier.Simplificar(saldos);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, t => Assert.Equal("Ana", t.Para));
        // As transferências pra Ana devem somar o saldo dela (~6.67), dentro da tolerância de arredondamento.
        Assert.True(Math.Abs(resultado.Sum(t => t.Valor) - saldoAna) < 0.02m);
    }

    [Fact]
    public void ListaVazia_NaoGeraTransferencia()
    {
        var resultado = DebtSimplifier.Simplificar([]);

        Assert.Empty(resultado);
    }
}
