using RachaContas.Bot.Models;
using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class ExportadorCsvTests
{
    private readonly ExportadorCsv _exportador = new();

    [Fact]
    public void CsvVazio_GeraHeaderApenas()
    {
        var despesas = new List<Despesa>();

        var stream = _exportador.GerarCsvDespesas(despesas);
        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        using var reader = new StreamReader(stream, encoding);
        var conteudo = reader.ReadToEnd();

        var linhas = conteudo.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(linhas);
        Assert.Contains("Data", conteudo);
        Assert.Contains("Descrição", conteudo);
        Assert.Contains("Quem Pagou", conteudo);
    }

    [Fact]
    public void UmaDespesaSimples_GeraCSVCorretamente()
    {
        var pagador = new Participante { Id = 1, Nome = "João", TelegramUserId = 123 };
        var despesa = new Despesa
        {
            Id = 1,
            Descricao = "Pizza",
            Valor = 50m,
            CriadoEm = new DateTime(2024, 7, 15, 18, 30, 0),
            PagoPor = pagador,
            PagoPorParticipanteId = 1,
            GrupoId = 1
        };

        var stream = _exportador.GerarCsvDespesas([despesa]);
        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        using var reader = new StreamReader(stream, encoding);
        var conteudo = reader.ReadToEnd();

        var linhas = conteudo.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, linhas.Length);

        Assert.Contains("15/07/2024", conteudo);
        Assert.Contains("Pizza", conteudo);
        Assert.Contains("João", conteudo);
        Assert.Contains("50.00", conteudo);
    }

    [Fact]
    public void DespesasComMoedaEstrangeira_ExportaValoresOriginais()
    {
        var pagador = new Participante { Id = 1, Nome = "Maria", TelegramUserId = 456 };
        var despesa = new Despesa
        {
            Id = 1,
            Descricao = "Hotel em NY",
            Valor = 200.50m,
            ValorOriginal = 40m,
            MoedaOriginal = "USD",
            TaxaCambio = 5.0125m,
            CriadoEm = new DateTime(2024, 7, 14, 10, 0, 0),
            PagoPor = pagador,
            PagoPorParticipanteId = 1,
            GrupoId = 1
        };

        var stream = _exportador.GerarCsvDespesas([despesa]);
        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        using var reader = new StreamReader(stream, encoding);
        var conteudo = reader.ReadToEnd();

        Assert.Contains("40.00", conteudo);
        Assert.Contains("USD", conteudo);
        Assert.Contains("200.50", conteudo);
    }

    [Fact]
    public void DespesasComCaracteresEspeciais_EscapaCorretamente()
    {
        var pagador = new Participante { Id = 1, Nome = "Ana Silva", TelegramUserId = 789 };
        var despesa = new Despesa
        {
            Id = 1,
            Descricao = "Almoço, cafeteria \"Bom Gosto\"",
            Valor = 35m,
            CriadoEm = new DateTime(2024, 7, 13, 12, 0, 0),
            PagoPor = pagador,
            PagoPorParticipanteId = 1,
            GrupoId = 1
        };

        var stream = _exportador.GerarCsvDespesas([despesa]);
        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        using var reader = new StreamReader(stream, encoding);
        var conteudo = reader.ReadToEnd();

        Assert.Contains("\"Almoço, cafeteria \"\"Bom Gosto\"\"\"", conteudo);
    }

    [Fact]
    public void VariasDespesas_ExportaTodas()
    {
        var pagador1 = new Participante { Id = 1, Nome = "João", TelegramUserId = 123 };
        var pagador2 = new Participante { Id = 2, Nome = "Maria", TelegramUserId = 456 };

        var despesas = new List<Despesa>
        {
            new()
            {
                Id = 1,
                Descricao = "Mercado",
                Valor = 150m,
                CriadoEm = new DateTime(2024, 7, 15, 10, 0, 0),
                PagoPor = pagador1,
                PagoPorParticipanteId = 1,
                GrupoId = 1
            },
            new()
            {
                Id = 2,
                Descricao = "Gasolina",
                Valor = 80m,
                CriadoEm = new DateTime(2024, 7, 14, 15, 30, 0),
                PagoPor = pagador2,
                PagoPorParticipanteId = 2,
                GrupoId = 1
            },
            new()
            {
                Id = 3,
                Descricao = "Restaurante",
                Valor = 120m,
                CriadoEm = new DateTime(2024, 7, 13, 19, 0, 0),
                PagoPor = pagador1,
                PagoPorParticipanteId = 1,
                GrupoId = 1
            }
        };

        var stream = _exportador.GerarCsvDespesas(despesas);
        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        using var reader = new StreamReader(stream, encoding);
        var conteudo = reader.ReadToEnd();

        var linhas = conteudo.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, linhas.Length);

        Assert.Contains("Mercado", conteudo);
        Assert.Contains("Gasolina", conteudo);
        Assert.Contains("Restaurante", conteudo);
        Assert.Contains("150.00", conteudo);
        Assert.Contains("80.00", conteudo);
        Assert.Contains("120.00", conteudo);
    }

    [Fact]
    public void CSVGerado_EhValidoParaExcel()
    {
        var pagador = new Participante { Id = 1, Nome = "Test", TelegramUserId = 999 };
        var despesa = new Despesa
        {
            Id = 1,
            Descricao = "Teste CSV",
            Valor = 99.99m,
            CriadoEm = new DateTime(2024, 7, 15, 12, 0, 0),
            PagoPor = pagador,
            PagoPorParticipanteId = 1,
            GrupoId = 1
        };

        var stream = _exportador.GerarCsvDespesas([despesa]);
        var bytes = stream.ToArray();

        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        var conteudo = encoding.GetString(bytes);
        Assert.NotEmpty(conteudo);

        var linhas = conteudo.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(linhas.Length >= 2, "CSV deve ter cabeçalho + dados");

        var campos = linhas[0].Split(",");
        Assert.True(campos.Length >= 6, "Cabeçalho deve ter pelo menos 6 campos");

        var camposDados = linhas[1].Split(",");
        Assert.True(camposDados.Length >= 5, "Dados devem ter pelo menos 5 campos");
    }

    [Fact]
    public void DespesaSemPagador_UsaTextoDefault()
    {
        var despesa = new Despesa
        {
            Id = 1,
            Descricao = "Despesa órfã",
            Valor = 50m,
            CriadoEm = new DateTime(2024, 7, 15, 12, 0, 0),
            PagoPor = null,
            PagoPorParticipanteId = 1,
            GrupoId = 1
        };

        var stream = _exportador.GerarCsvDespesas([despesa]);
        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        using var reader = new StreamReader(stream, encoding);
        var conteudo = reader.ReadToEnd();

        Assert.Contains("Desconhecido", conteudo);
        Assert.Contains("Despesa órfã", conteudo);
    }
}
