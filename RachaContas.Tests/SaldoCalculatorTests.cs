using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RachaContas.Bot.Data;
using RachaContas.Bot.Models;
using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

/// <summary>
/// Testes de integração de verdade contra um SQLite em memória — não é só o
/// algoritmo puro, aqui exercitamos EF Core + a lógica de saldo juntos.
/// </summary>
public class SaldoCalculatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly SaldoCalculator _calculator = new();

    public SaldoCalculatorTests()
    {
        // SQLite em memória some quando a conexão fecha — por isso mantemos ela aberta durante o teste.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UmaDespesaDividaAoMeio_CalculaSaldoCorretamente()
    {
        var grupo = new Grupo { TelegramChatId = 1 };
        var ana = new Participante { Grupo = grupo, TelegramUserId = 10, Nome = "Ana" };
        var bia = new Participante { Grupo = grupo, TelegramUserId = 20, Nome = "Bia" };
        _db.Grupos.Add(grupo);
        _db.Participantes.AddRange(ana, bia);
        await _db.SaveChangesAsync();

        var despesa = new Despesa
        {
            GrupoId = grupo.Id,
            PagoPorParticipanteId = ana.Id,
            Descricao = "Aluguel",
            Valor = 100m,
        };
        despesa.Participantes.Add(new DespesaParticipante { ParticipanteId = ana.Id });
        despesa.Participantes.Add(new DespesaParticipante { ParticipanteId = bia.Id });
        _db.Despesas.Add(despesa);
        await _db.SaveChangesAsync();

        var saldos = await _calculator.CalcularAsync(_db, grupo.Id);

        Assert.Equal(2, saldos.Count);
        Assert.Equal(50m, saldos.Single(s => s.Nome == "Ana").Valor);
        Assert.Equal(-50m, saldos.Single(s => s.Nome == "Bia").Valor);
    }

    [Fact]
    public async Task VariasDespesas_AcumulaSaldoCorretamente()
    {
        var grupo = new Grupo { TelegramChatId = 2 };
        var ana = new Participante { Grupo = grupo, TelegramUserId = 30, Nome = "Ana" };
        var bia = new Participante { Grupo = grupo, TelegramUserId = 40, Nome = "Bia" };
        var caio = new Participante { Grupo = grupo, TelegramUserId = 50, Nome = "Caio" };
        _db.Grupos.Add(grupo);
        _db.Participantes.AddRange(ana, bia, caio);
        await _db.SaveChangesAsync();

        // Ana paga 90 (dividido em 3 = 30 cada) e depois Bia paga 30 (dividido em 3 = 10 cada).
        var d1 = new Despesa { GrupoId = grupo.Id, PagoPorParticipanteId = ana.Id, Descricao = "Mercado", Valor = 90m };
        d1.Participantes.Add(new DespesaParticipante { ParticipanteId = ana.Id });
        d1.Participantes.Add(new DespesaParticipante { ParticipanteId = bia.Id });
        d1.Participantes.Add(new DespesaParticipante { ParticipanteId = caio.Id });

        var d2 = new Despesa { GrupoId = grupo.Id, PagoPorParticipanteId = bia.Id, Descricao = "Gasolina", Valor = 30m };
        d2.Participantes.Add(new DespesaParticipante { ParticipanteId = ana.Id });
        d2.Participantes.Add(new DespesaParticipante { ParticipanteId = bia.Id });
        d2.Participantes.Add(new DespesaParticipante { ParticipanteId = caio.Id });

        _db.Despesas.AddRange(d1, d2);
        await _db.SaveChangesAsync();

        var saldos = await _calculator.CalcularAsync(_db, grupo.Id);

        // Ana: +90 -30 -10 = +50 | Bia: -30 +30 -10 = -10 | Caio: -30 -10 = -40
        Assert.Equal(50m, saldos.Single(s => s.Nome == "Ana").Valor);
        Assert.Equal(-10m, saldos.Single(s => s.Nome == "Bia").Valor);
        Assert.Equal(-40m, saldos.Single(s => s.Nome == "Caio").Valor);
    }

    [Fact]
    public async Task GrupoSemDespesas_TodosComSaldoZero()
    {
        var grupo = new Grupo { TelegramChatId = 3 };
        var ana = new Participante { Grupo = grupo, TelegramUserId = 60, Nome = "Ana" };
        _db.Grupos.Add(grupo);
        _db.Participantes.Add(ana);
        await _db.SaveChangesAsync();

        var saldos = await _calculator.CalcularAsync(_db, grupo.Id);

        Assert.Equal(0m, Assert.Single(saldos).Valor);
    }
}
