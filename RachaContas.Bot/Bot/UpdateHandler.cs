using System.Collections.Concurrent;
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
    private enum EtapaFluxo { EscolhendoParaExcluir, EscolhendoParaEditar, AguardandoNovoTexto, ConfirmandoLimpeza }

    private record EstadoConversa(EtapaFluxo Etapa, IReadOnlyList<int> DespesaIds, int? DespesaIdEscolhida = null);

    // Estado por (chat, usuário) de um fluxo /editar ou /excluir em andamento.
    // UpdateHandler é singleton, então isso sobrevive entre mensagens — mas
    // some se o bot reiniciar (aceitável: é só um "estou no meio de uma ação").
    private readonly ConcurrentDictionary<(long ChatId, long UserId), EstadoConversa> _estados = new();

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        var message = update.Message;
        if (message?.Text is not { } texto || message.From is null)
            return;

        var chatId = message.Chat.Id;
        var chave = (chatId, message.From.Id);

        try
        {
            string? resposta;

            if (_estados.TryGetValue(chave, out var estado))
            {
                resposta = await ContinuarFluxoAsync(chave, estado, texto, ct);
            }
            else
            {
                var comando = texto.Split(' ', 2)[0].ToLowerInvariant();
                resposta = comando switch
                {
                    "/start" => Boas_Vindas,
                    "/entrar" => await EntrarAsync(chatId, message.From, ct),
                    "/despesa" => await DespesaAsync(chatId, message.From, texto, ct),
                    "/desfazer" => await DesfazerAsync(chatId, message.From, ct),
                    "/editar" => await IniciarEdicaoAsync(chave, chatId, ct),
                    "/excluir" => await IniciarExclusaoAsync(chave, chatId, ct),
                    "/limpar" => await IniciarLimpezaAsync(chave, chatId, ct),
                    "/cancelar" => CancelarFluxo(chave),
                    "/saldo" => await SaldoAsync(chatId, ct),
                    "/fechar" => await FecharAsync(chatId, ct),
                    "/extrato" => await ExtratoAsync(chatId, ct),
                    _ => (string?)null,
                };
            }

            if (resposta is not null)
                await botClient.SendMessage(chatId, resposta, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha processando mensagem no chat {ChatId}", chatId);
            _estados.TryRemove(chave, out _);
            await botClient.SendMessage(chatId, "Deu ruim processando isso. Tenta de novo?", cancellationToken: ct);
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
        "/desfazer — apaga a sua última despesa lançada\n" +
        "/editar — corrige um lançamento (mostra a lista, você escolhe o número)\n" +
        "/excluir — remove um lançamento (mostra a lista, você escolhe o número)\n" +
        "/limpar — apaga TODAS as despesas do grupo (pede confirmação)\n" +
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

    private async Task<string> DesfazerAsync(long chatId, User from, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Não tem nada pra desfazer ainda.";

        var pagador = await db.Participantes.FirstOrDefaultAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == from.Id, ct);
        if (pagador is null)
            return "Você ainda não lançou nenhuma despesa.";

        var ultima = await db.Despesas
            .Where(d => d.GrupoId == grupo.Id && d.PagoPorParticipanteId == pagador.Id)
            .OrderByDescending(d => d.CriadoEm)
            .FirstOrDefaultAsync(ct);

        if (ultima is null)
            return "Você não tem nenhuma despesa lançada pra desfazer.";

        db.Despesas.Remove(ultima);
        await db.SaveChangesAsync(ct);

        return $"Desfeito: \"{ultima.Descricao}\" de {ultima.Valor:C}.";
    }

    private Task<string> ContinuarFluxoAsync((long ChatId, long UserId) chave, EstadoConversa estado, string texto, CancellationToken ct)
    {
        if (texto.Trim().Equals("/cancelar", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(CancelarFluxo(chave));

        return estado.Etapa switch
        {
            EtapaFluxo.EscolhendoParaExcluir => ConcluirExclusaoAsync(chave, estado, texto, ct),
            EtapaFluxo.EscolhendoParaEditar => Task.FromResult(AvancarParaTextoDeEdicao(chave, estado, texto)),
            EtapaFluxo.AguardandoNovoTexto => ConcluirEdicaoAsync(chave, estado, texto, ct),
            EtapaFluxo.ConfirmandoLimpeza => ConcluirLimpezaAsync(chave, chave.ChatId, texto, ct),
            _ => Task.FromResult("Deu ruim no meio do fluxo — manda /cancelar e tenta de novo."),
        };
    }

    private string CancelarFluxo((long ChatId, long UserId) chave) =>
        _estados.TryRemove(chave, out _) ? "Cancelado." : "Não tem nada pra cancelar.";

    private async Task<string> IniciarExclusaoAsync((long ChatId, long UserId) chave, long chatId, CancellationToken ct)
    {
        var (lista, resposta) = await ListarParaEscolhaAsync(chatId, ct);
        if (lista is null)
            return resposta;

        _estados[chave] = new EstadoConversa(EtapaFluxo.EscolhendoParaExcluir, lista);
        return resposta + "\n\nManda o número do lançamento que quer excluir (ou /cancelar).";
    }

    private async Task<string> IniciarEdicaoAsync((long ChatId, long UserId) chave, long chatId, CancellationToken ct)
    {
        var (lista, resposta) = await ListarParaEscolhaAsync(chatId, ct);
        if (lista is null)
            return resposta;

        _estados[chave] = new EstadoConversa(EtapaFluxo.EscolhendoParaEditar, lista);
        return resposta + "\n\nManda o número do lançamento que quer editar (ou /cancelar).";
    }

    private async Task<string> IniciarLimpezaAsync((long ChatId, long UserId) chave, long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Não tem nada pra limpar ainda.";

        var total = await db.Despesas.CountAsync(d => d.GrupoId == grupo.Id, ct);
        if (total == 0)
            return "Não tem nenhuma despesa lançada ainda.";

        _estados[chave] = new EstadoConversa(EtapaFluxo.ConfirmandoLimpeza, []);
        return $"Isso vai apagar {total} despesa(s) lançada(s) nesse grupo — não dá pra desfazer depois. " +
               "Quem já entrou na resenha continua registrado. Manda /confirmar pra seguir, ou /cancelar.";
    }

    private async Task<string> ConcluirLimpezaAsync((long ChatId, long UserId) chave, long chatId, string texto, CancellationToken ct)
    {
        _estados.TryRemove(chave, out _);

        if (!texto.Trim().Equals("/confirmar", StringComparison.OrdinalIgnoreCase))
            return "Limpeza cancelada — nada foi apagado.";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Não tinha nada pra limpar mesmo.";

        var despesas = await db.Despesas.Where(d => d.GrupoId == grupo.Id).ToListAsync(ct);
        db.Despesas.RemoveRange(despesas);
        await db.SaveChangesAsync(ct);

        return $"Pronto — {despesas.Count} despesa(s) apagada(s). Quem já tinha entrado continua registrado.";
    }

    private async Task<(List<int>? Lista, string Resposta)> ListarParaEscolhaAsync(long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return (null, "Não tem despesa nenhuma lançada ainda.");

        var despesas = await db.Despesas
            .Where(d => d.GrupoId == grupo.Id)
            .Include(d => d.PagoPor)
            .OrderByDescending(d => d.CriadoEm)
            .Take(15)
            .ToListAsync(ct);

        if (despesas.Count == 0)
            return (null, "Não tem despesa nenhuma lançada ainda.");

        var linhas = despesas.Select((d, i) => $"{i + 1}. {d.PagoPor?.Nome} pagou {d.Valor:C} — {d.Descricao} ({d.CriadoEm:dd/MM})");
        return (despesas.Select(d => d.Id).ToList(), string.Join('\n', linhas));
    }

    private async Task<string> ConcluirExclusaoAsync((long ChatId, long UserId) chave, EstadoConversa estado, string texto, CancellationToken ct)
    {
        if (!int.TryParse(texto.Trim(), out var numero) || numero < 1 || numero > estado.DespesaIds.Count)
            return $"Manda um número de 1 a {estado.DespesaIds.Count} (ou /cancelar).";

        var despesaId = estado.DespesaIds[numero - 1];
        _estados.TryRemove(chave, out _);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var despesa = await db.Despesas.FindAsync([despesaId], ct);
        if (despesa is null)
            return "Esse lançamento já não existe mais.";

        db.Despesas.Remove(despesa);
        await db.SaveChangesAsync(ct);

        return $"Excluído: \"{despesa.Descricao}\" de {despesa.Valor:C}.";
    }

    private string AvancarParaTextoDeEdicao((long ChatId, long UserId) chave, EstadoConversa estado, string texto)
    {
        if (!int.TryParse(texto.Trim(), out var numero) || numero < 1 || numero > estado.DespesaIds.Count)
            return $"Manda um número de 1 a {estado.DespesaIds.Count} (ou /cancelar).";

        var despesaId = estado.DespesaIds[numero - 1];
        _estados[chave] = estado with { Etapa = EtapaFluxo.AguardandoNovoTexto, DespesaIdEscolhida = despesaId };

        return "Manda o lançamento corrigido, no formato: <valor> <descrição>. Ex.: 34,22 uber pro zoológico";
    }

    private async Task<string> ConcluirEdicaoAsync((long ChatId, long UserId) chave, EstadoConversa estado, string texto, CancellationToken ct)
    {
        _estados.TryRemove(chave, out _);

        if (!TentarExtrairValorEDescricaoLivre(texto, out var valor, out var descricao) || valor <= 0)
            return "Formato inválido — o lançamento não foi alterado. Manda /editar de novo se quiser tentar outra vez.";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var despesa = await db.Despesas.FindAsync([estado.DespesaIdEscolhida!.Value], ct);
        if (despesa is null)
            return "Esse lançamento já não existe mais.";

        despesa.Valor = valor;
        despesa.Descricao = descricao;
        await db.SaveChangesAsync(ct);

        return $"Corrigido: agora é {valor:C} ({descricao}).";
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
    private static bool TentarExtrairValorEDescricao(string texto, out decimal valor, out string descricao) =>
        TentarExtrairValorEDescricaoDoIndice(texto, indiceInicial: 1, out valor, out descricao);

    /// <summary>Mesma extração, mas pra texto livre sem o "/despesa" na frente — usado pelo fluxo de /editar.</summary>
    private static bool TentarExtrairValorEDescricaoLivre(string texto, out decimal valor, out string descricao) =>
        TentarExtrairValorEDescricaoDoIndice(texto, indiceInicial: 0, out valor, out descricao);

    private static bool TentarExtrairValorEDescricaoDoIndice(string texto, int indiceInicial, out decimal valor, out string descricao)
    {
        valor = 0;
        descricao = string.Empty;

        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < indiceInicial + 2)
            return false;

        var indiceValor = indiceInicial;
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

        if (!ValorParser.TentarParsear(tokenValor, out valor))
            return false;

        descricao = string.Join(' ', partes.Skip(indiceValor + 1));
        return !string.IsNullOrWhiteSpace(descricao);
    }

}
