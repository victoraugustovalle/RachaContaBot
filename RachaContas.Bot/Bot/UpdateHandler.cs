using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RachaContas.Bot.Data;
using RachaContas.Bot.Models;
using RachaContas.Bot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RachaContas.Bot.Bot;

public class UpdateHandler(
    IDbContextFactory<AppDbContext> dbFactory,
    SaldoCalculator saldoCalculator,
    ILogger<UpdateHandler> logger) : IUpdateHandler
{
    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        var message = update.Message;
        if (message?.Text is not { } texto || message.From is null)
            return;

        var chatId = message.Chat.Id;
        var comando = texto.Split(' ', 2)[0].ToLowerInvariant();

        try
        {
            var resposta = comando switch
            {
                "/start" => Boas_Vindas,
                "/entrar" => await EntrarAsync(chatId, message.From, ct),
                "/despesa" => await DespesaAsync(chatId, message.From, texto, ct),
                "/saldo" => await SaldoAsync(chatId, ct),
                "/fechar" => await FecharAsync(chatId, ct),
                "/extrato" => await ExtratoAsync(chatId, ct),
                _ => (string?)null,
            };

            if (resposta is not null)
                await botClient.SendMessage(chatId, resposta, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha processando o comando {Comando} no chat {ChatId}", comando, chatId);
            await botClient.SendMessage(chatId, "Deu ruim processando esse comando. Tenta de novo?", cancellationToken: ct);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        logger.LogError(exception, "Erro no polling do Telegram ({Source})", source);
        return Task.CompletedTask;
    }

    private const string Boas_Vindas =
        "Oi! Eu divido as contas do grupo pra você.\n\n" +
        "/entrar — entra na resenha\n" +
        "/despesa <valor> <descrição> — lança um gasto, dividido igual entre quem já entrou\n" +
        "/saldo — quem deve e quem tem a receber\n" +
        "/fechar — calcula as transferências mínimas pra quitar tudo\n" +
        "/extrato — últimos gastos lançados";

    private async Task<string> EntrarAsync(long chatId, User from, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterOuCriarGrupoAsync(db, chatId, ct);
        var nome = NomeDe(from);

        var jaExiste = await db.Participantes.AnyAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == from.Id, ct);
        if (jaExiste)
            return $"{nome} já tá na resenha.";

        db.Participantes.Add(new Participante { GrupoId = grupo.Id, TelegramUserId = from.Id, Nome = nome });
        await db.SaveChangesAsync(ct);

        return $"{nome} entrou! Use /despesa quando pagar alguma coisa pelo grupo.";
    }

    private async Task<string> DespesaAsync(long chatId, User from, string texto, CancellationToken ct)
    {
        if (!TentarExtrairValorEDescricao(texto, out var valor, out var descricao) || valor <= 0)
            return "Formato: /despesa <valor> <descrição>. Ex.: /despesa 45,50 Uber pro aeroporto";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterOuCriarGrupoAsync(db, chatId, ct);

        var pagador = await db.Participantes.FirstOrDefaultAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == from.Id, ct);
        if (pagador is null)
            return $"{NomeDe(from)}, manda /entrar primeiro antes de lançar uma despesa.";

        var participantesDoGrupo = await db.Participantes.Where(p => p.GrupoId == grupo.Id).ToListAsync(ct);

        var despesa = new Despesa
        {
            GrupoId = grupo.Id,
            PagoPorParticipanteId = pagador.Id,
            Descricao = descricao,
            Valor = valor,
        };
        despesa.Participantes = participantesDoGrupo
            .Select(p => new DespesaParticipante { ParticipanteId = p.Id })
            .ToList();

        db.Despesas.Add(despesa);
        await db.SaveChangesAsync(ct);

        var cota = valor / participantesDoGrupo.Count;
        var pessoas = participantesDoGrupo.Count == 1 ? "1 pessoa (só você por enquanto)" : $"{participantesDoGrupo.Count} pessoas";
        return $"Beleza! {NomeDe(from)} pagou {valor:C} em \"{descricao}\", dividido entre {pessoas} ({cota:C} cada).";
    }

    private async Task<string> SaldoAsync(long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Ainda não tem ninguém na resenha — manda /entrar primeiro.";

        var saldos = await saldoCalculator.CalcularAsync(db, grupo.Id, ct);
        if (saldos.Count == 0)
            return "Ninguém entrou na resenha ainda.";

        var linhas = saldos.Select(s => s.Valor switch
        {
            > 0.01m => $"{s.Nome}: tem {s.Valor:C} a receber",
            < -0.01m => $"{s.Nome}: deve {-s.Valor:C}",
            _ => $"{s.Nome}: tá quite",
        });

        return "Saldos:\n" + string.Join('\n', linhas);
    }

    private async Task<string> FecharAsync(long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Ainda não tem ninguém na resenha — manda /entrar primeiro.";

        var saldos = await saldoCalculator.CalcularAsync(db, grupo.Id, ct);
        var transferencias = DebtSimplifier.Simplificar(saldos);

        if (transferencias.Count == 0)
            return "Já tá tudo quite — ninguém deve nada pra ninguém.";

        var linhas = transferencias.Select(t => $"{t.De} paga {t.Valor:C} pra {t.Para}");
        return "Pra fechar a conta:\n" + string.Join('\n', linhas);
    }

    private async Task<string> ExtratoAsync(long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Ainda não tem despesa nenhuma — manda /entrar e depois /despesa.";

        var despesas = await db.Despesas
            .Where(d => d.GrupoId == grupo.Id)
            .Include(d => d.PagoPor)
            .OrderByDescending(d => d.CriadoEm)
            .Take(10)
            .ToListAsync(ct);

        if (despesas.Count == 0)
            return "Nenhuma despesa lançada ainda.";

        var linhas = despesas.Select(d => $"{d.CriadoEm:dd/MM} — {d.PagoPor?.Nome} pagou {d.Valor:C} ({d.Descricao})");
        return "Últimas despesas:\n" + string.Join('\n', linhas);
    }

    private static async Task<Grupo> ObterOuCriarGrupoAsync(AppDbContext db, long chatId, CancellationToken ct)
    {
        var grupo = await db.Grupos.FirstOrDefaultAsync(g => g.TelegramChatId == chatId, ct);
        if (grupo is not null)
            return grupo;

        grupo = new Grupo { TelegramChatId = chatId };
        db.Grupos.Add(grupo);
        await db.SaveChangesAsync(ct);
        return grupo;
    }

    private static Task<Grupo?> ObterGrupoAsync(AppDbContext db, long chatId, CancellationToken ct) =>
        db.Grupos.FirstOrDefaultAsync(g => g.TelegramChatId == chatId, ct);

    private static string NomeDe(User user) =>
        string.IsNullOrWhiteSpace(user.LastName) ? user.FirstName : $"{user.FirstName} {user.LastName}";

    /// <summary>
    /// Extrai valor e descrição de "/despesa &lt;valor&gt; &lt;descrição&gt;", aceitando
    /// o "R$" opcional colado ("R$50,00") ou separado ("R$ 50,00") do número.
    /// </summary>
    private static bool TentarExtrairValorEDescricao(string texto, out decimal valor, out string descricao)
    {
        valor = 0;
        descricao = string.Empty;

        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 3)
            return false;

        var indiceValor = 1;
        var tokenValor = partes[indiceValor];

        if (tokenValor.Equals("R$", StringComparison.OrdinalIgnoreCase))
        {
            indiceValor++;
            if (indiceValor >= partes.Length)
                return false;
            tokenValor = partes[indiceValor];
        }
        else if (tokenValor.StartsWith("R$", StringComparison.OrdinalIgnoreCase))
        {
            tokenValor = tokenValor[2..];
        }

        if (!TentarParsearValor(tokenValor, out valor))
            return false;

        descricao = string.Join(' ', partes.Skip(indiceValor + 1));
        return !string.IsNullOrWhiteSpace(descricao);
    }

    private static bool TentarParsearValor(string texto, out decimal valor) =>
        decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out valor) ||
        decimal.TryParse(texto, NumberStyles.Number, new CultureInfo("pt-BR"), out valor);
}
