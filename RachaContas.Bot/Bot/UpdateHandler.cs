using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RachaContas.Bot.Data;
using RachaContas.Bot.Models;
using RachaContas.Bot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RachaContas.Bot.Bot;

public class UpdateHandler(
    IDbContextFactory<AppDbContext> dbFactory,
    SaldoCalculator saldoCalculator,
    CambioService cambioService,
    PagamentoService pagamentoService,
    SolicitacoesChavePixPendentes solicitacoesPendentes,
    ExportadorCsv exportadorCsv,
    ILogger<UpdateHandler> logger) : IUpdateHandler
{
    private enum EtapaFluxo { EscolhendoParaExcluir, EscolhendoParaEditar, AguardandoNovoTexto, ConfirmandoLimpeza, ConfirmandoMoedaEstrangeira }

    private record EstadoConversa(
        EtapaFluxo Etapa,
        IReadOnlyList<int> DespesaIds,
        int? DespesaIdEscolhida = null,
        string? MoedaPendente = null,
        decimal? ValorOriginalPendente = null,
        string? DescricaoPendente = null);

    // Estado por (chat, usuário) de um fluxo /editar ou /excluir em andamento.
    // UpdateHandler é singleton, então isso sobrevive entre mensagens — mas
    // some se o bot reiniciar (aceitável: é só um "estou no meio de uma ação").
    private readonly ConcurrentDictionary<(long ChatId, long UserId), EstadoConversa> _estados = new();

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            await TratarCallbackQueryAsync(botClient, callbackQuery, ct);
            return;
        }

        var message = update.Message;
        if (message?.Text is not { } texto || message.From is null)
            return;

        var chatId = message.Chat.Id;
        var chave = (chatId, message.From.Id);

        try
        {
            string? resposta;

            if (message.Chat.Type == ChatType.Private && solicitacoesPendentes.TryConsumirNome(message.From.Id, out var participanteIdAguardandoNome))
            {
                var nomeCompleto = texto.Trim();
                if (string.IsNullOrWhiteSpace(nomeCompleto) || nomeCompleto.StartsWith('/'))
                {
                    solicitacoesPendentes.DevolverNome(message.From.Id, participanteIdAguardandoNome);
                    resposta = "Manda seu nome completo (não pode ser vazio nem um comando).";
                }
                else
                {
                    await pagamentoService.SalvarNomeCompletoAsync(participanteIdAguardandoNome, nomeCompleto, ct);
                    resposta = await pagamentoService.GerarCobrancasPendentesAsync(participanteIdAguardandoNome, ct);
                }
            }
            else if (message.Chat.Type == ChatType.Private && solicitacoesPendentes.TryConsumirChave(message.From.Id, out var participanteIdAguardandoChave))
            {
                if (ChavePixParser.TentarExtrair(texto, out var chaveNormalizada, out var erroChave))
                {
                    var temNomeCompleto = await pagamentoService.SalvarChavePixAsync(participanteIdAguardandoChave, chaveNormalizada, ct);
                    if (temNomeCompleto)
                    {
                        resposta = await pagamentoService.GerarCobrancasPendentesAsync(participanteIdAguardandoChave, ct);
                    }
                    else
                    {
                        solicitacoesPendentes.RegistrarNome(message.From.Id, participanteIdAguardandoChave);
                        resposta = "Chave Pix salva! Agora manda seu nome completo — é o que vai aparecer pro pagador no Pix.";
                    }
                }
                else
                {
                    solicitacoesPendentes.DevolverChave(message.From.Id, participanteIdAguardandoChave);
                    resposta = erroChave;
                }
            }
            else if (_estados.TryGetValue(chave, out var estado))
            {
                resposta = await ContinuarFluxoAsync(chave, estado, texto, ct);
            }
            else
            {
                var comando = texto.Split(' ', 2)[0].ToLowerInvariant();
                resposta = comando switch
                {
                    "/start" => BoasVindas,
                    "/entrar" => await EntrarAsync(chatId, message.From, texto, ct),
                    "/despesa" => await DespesaAsync(chatId, message.From, texto, ct),
                    "/desfazer" => await DesfazerAsync(chatId, message.From, ct),
                    "/editar" => await IniciarEdicaoAsync(chave, chatId, ct),
                    "/excluir" => await IniciarExclusaoAsync(chave, chatId, ct),
                    "/limpar" => await IniciarLimpezaAsync(chave, chatId, ct),
                    "/cancelar" => CancelarFluxo(chave),
                    "/saldo" => await SaldoAsync(chatId, ct),
                    "/fechar" => await FecharAsync(chatId, ct),
                    "/pagar" => await PagarAsync(chatId, ct),
                    "/chavepix" => await DefinirChavePixAsync(chatId, message.From, texto, ct),
                    "/extrato" => await ExtratoAsync(chatId, ct),
                    "/extrato_csv" => await ExtratoCSVAsync(botClient, chatId, ct) ?? "Extrato enviado!",
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

    private const string BoasVindas =
        "Oi! Eu divido as contas do grupo pra você.\n\n" +
        "/entrar — entra na resenha\n" +
        "/entrar <nome> — adiciona alguém manualmente (pra quem não usa o Telegram ou não vai mandar o comando)\n" +
        "/despesa <valor> <descrição> — lança um gasto, dividido igual entre quem já entrou " +
        "(aceita moeda estrangeira, ex.: $50 uber — eu confirmo e converto pra você)\n" +
        "/desfazer — apaga a sua última despesa lançada\n" +
        "/editar — corrige um lançamento (mostra a lista, você escolhe o número)\n" +
        "/excluir — remove um lançamento (mostra a lista, você escolhe o número)\n" +
        "/limpar — apaga TODAS as despesas do grupo (pede confirmação)\n" +
        "/saldo — quem deve e quem tem a receber\n" +
        "/fechar — calcula as transferências mínimas pra quitar tudo\n" +
        "/pagar — gera os Pix pra quitar o que o /fechar calculou (opcional)\n" +
        "/chavepix <tipo> <chave> — cadastra ou corrige sua chave Pix (tipo: telefone, cpf, email ou aleatoria)\n" +
        "/extrato — últimos gastos lançados\n" +
        "/extrato_csv — exporta Todas as despesas em Formato CSV";

    private async Task<string> EntrarAsync(long chatId, User from, string texto, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterOuCriarGrupoAsync(db, chatId, ct);

        var nomeManual = texto.Split(' ', 2) is [_, var resto] && !string.IsNullOrWhiteSpace(resto) ? resto.Trim() : null;
        if (nomeManual is not null)
            return await EntrarManualAsync(db, grupo, nomeManual, ct);

        var nome = NomeDe(from);

        var jaExiste = await db.Participantes.AnyAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == from.Id, ct);
        if (jaExiste)
            return $"{nome} já tá na resenha.";

        db.Participantes.Add(new Participante { GrupoId = grupo.Id, TelegramUserId = from.Id, Nome = nome });
        await db.SaveChangesAsync(ct);

        return $"{nome} entrou! Use /despesa quando pagar alguma coisa pelo grupo.";
    }

    // "/entrar <nome>" adiciona alguém que não vai (ou não pode) mandar o comando pelo
    // próprio Telegram. Ganha um Id negativo (nunca colide com um TelegramUserId de
    // verdade, que é sempre positivo) só pra existir na tabela; entra nas divisões
    // normalmente, mas não recebe nada do bot no privado (nem Pix) porque não tem
    // conta de verdade por trás.
    private static async Task<string> EntrarManualAsync(AppDbContext db, Grupo grupo, string nome, CancellationToken ct)
    {
        var jaExiste = await db.Participantes.AnyAsync(p => p.GrupoId == grupo.Id && p.Nome == nome, ct);
        if (jaExiste)
            return $"{nome} já tá na resenha.";

        var menorIdAtual = await db.Participantes
            .Where(p => p.GrupoId == grupo.Id)
            .Select(p => (long?)p.TelegramUserId)
            .MinAsync(ct) ?? 0;
        var idManual = Math.Min(-1, menorIdAtual - 1);

        db.Participantes.Add(new Participante { GrupoId = grupo.Id, TelegramUserId = idManual, Nome = nome });
        await db.SaveChangesAsync(ct);

        return $"{nome} entrou na resenha (adicionado manualmente — não recebe mensagens do bot, então pode entrar nas despesas mas não lança nem recebe Pix sozinho).";
    }

    private async Task<string> DespesaAsync(long chatId, User from, string texto, CancellationToken ct)
    {
        if (!TentarExtrairDespesa(texto, out var valor, out var moeda, out var descricao) || valor <= 0)
            return "Formato: /despesa <valor> <descrição>. Ex.: /despesa 45,50 Uber pro aeroporto";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterOuCriarGrupoAsync(db, chatId, ct);

        var pagador = await db.Participantes.FirstOrDefaultAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == from.Id, ct);
        if (pagador is null)
            return $"{NomeDe(from)}, manda /entrar primeiro antes de lançar uma despesa.";

        if (moeda is not null)
        {
            var chave = (chatId, from.Id);
            _estados[chave] = new EstadoConversa(EtapaFluxo.ConfirmandoMoedaEstrangeira, [],
                MoedaPendente: moeda, ValorOriginalPendente: valor, DescricaoPendente: descricao);

            return $"Essa despesa foi lançada em {NomeDaMoeda(moeda)} ({FormatarValorEstrangeiro(valor, moeda)}) — " +
                   "confirma? Manda /confirmar pra seguir, ou /cancelar.";
        }

        return await CriarDespesaAsync(db, grupo, pagador, descricao, valor, null, null, null, ct);
    }

    private async Task<string> ConcluirConfirmacaoMoedaAsync((long ChatId, long UserId) chave, EstadoConversa estado, string texto, CancellationToken ct)
    {
        if (!texto.Trim().Equals("/confirmar", StringComparison.OrdinalIgnoreCase))
        {
            _estados.TryRemove(chave, out _);
            return "Lançamento cancelado — nada foi salvo.";
        }

        var taxa = await cambioService.ObterTaxaParaBrlAsync(estado.MoedaPendente!, ct);
        if (taxa is null)
            return "Não consegui buscar a cotação agora — manda /confirmar de novo em instantes, ou /cancelar.";

        _estados.TryRemove(chave, out _);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterOuCriarGrupoAsync(db, chave.ChatId, ct);
        var pagador = await db.Participantes.FirstOrDefaultAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == chave.UserId, ct);
        if (pagador is null)
            return "Você saiu da resenha antes de confirmar — o lançamento foi perdido.";

        var valorBrl = Math.Round(estado.ValorOriginalPendente!.Value * taxa.Value, 2);
        return await CriarDespesaAsync(db, grupo, pagador, estado.DescricaoPendente!, valorBrl,
            estado.MoedaPendente, estado.ValorOriginalPendente, taxa, ct);
    }

    private static async Task<string> CriarDespesaAsync(
        AppDbContext db, Grupo grupo, Participante pagador, string descricao, decimal valorBrl,
        string? moedaOriginal, decimal? valorOriginal, decimal? taxaCambio, CancellationToken ct)
    {
        var participantesDoGrupo = await db.Participantes.Where(p => p.GrupoId == grupo.Id).ToListAsync(ct);

        var despesa = new Despesa
        {
            GrupoId = grupo.Id,
            PagoPorParticipanteId = pagador.Id,
            Descricao = descricao,
            Valor = valorBrl,
            MoedaOriginal = moedaOriginal,
            ValorOriginal = valorOriginal,
            TaxaCambio = taxaCambio,
        };
        despesa.Participantes = participantesDoGrupo
            .Select(p => new DespesaParticipante { ParticipanteId = p.Id })
            .ToList();

        db.Despesas.Add(despesa);
        await db.SaveChangesAsync(ct);

        var cota = valorBrl / participantesDoGrupo.Count;
        var pessoas = participantesDoGrupo.Count == 1 ? "1 pessoa (só você por enquanto)" : $"{participantesDoGrupo.Count} pessoas";
        var origem = moedaOriginal is not null ? $" ({FormatarValorEstrangeiro(valorOriginal!.Value, moedaOriginal)} convertido)" : "";

        return $"Beleza! {pagador.Nome} pagou {valorBrl:C}{origem} em \"{descricao}\", dividido entre {pessoas} ({cota:C} cada).";
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
            EtapaFluxo.ConfirmandoMoedaEstrangeira => ConcluirConfirmacaoMoedaAsync(chave, estado, texto, ct),
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

        // "moeda" é ignorada de propósito: edição sempre vira BRL puro (ver comentário abaixo).
        if (!TentarExtrairValorEDescricaoLivre(texto, out var valor, out _, out var descricao) || valor <= 0)
            return "Formato inválido — o lançamento não foi alterado. Manda /editar de novo se quiser tentar outra vez.";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var despesa = await db.Despesas.FindAsync([estado.DespesaIdEscolhida!.Value], ct);
        if (despesa is null)
            return "Esse lançamento já não existe mais.";

        despesa.Valor = valor;
        despesa.Descricao = descricao;
        // A edição vira a nova verdade em BRL — não mantém moeda/cotação antigas, que ficariam inconsistentes.
        despesa.MoedaOriginal = null;
        despesa.ValorOriginal = null;
        despesa.TaxaCambio = null;
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

        var resposta = "Saldos:\n" + string.Join('\n', linhas);

        var ultimoFechamento = await db.Fechamentos
            .Where(f => f.GrupoId == grupo.Id)
            .OrderByDescending(f => f.CriadoEm)
            .Include(f => f.Cobrancas).ThenInclude(c => c.Devedor)
            .Include(f => f.Cobrancas).ThenInclude(c => c.Credor)
            .FirstOrDefaultAsync(ct);

        if (ultimoFechamento is { Cobrancas.Count: > 0 })
        {
            var linhasPagamento = ultimoFechamento.Cobrancas.Select(c =>
            {
                var status = c.Status switch
                {
                    StatusCobranca.Paga => "✅ pago",
                    StatusCobranca.Cancelada => "❌ cancelado",
                    _ => "⏳ aguardando pagamento",
                };
                return $"{c.Devedor?.Nome} → {c.Credor?.Nome} ({c.Valor:C}): {status}";
            });

            resposta += "\n\nPagamentos do último /pagar:\n" + string.Join('\n', linhasPagamento);
        }

        return resposta;
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
        return "Pra fechar a conta:\n" + string.Join('\n', linhas) +
               "\n\nQuer que eu gere os Pix pra fechar isso? Manda /pagar.";
    }

    private async Task<string> PagarAsync(long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Ainda não tem ninguém na resenha — manda /entrar primeiro.";

        return await pagamentoService.ProcessarPagamentoAsync(grupo.Id, chatId, ct);
    }

    private async Task<string> DefinirChavePixAsync(long chatId, User from, string texto, CancellationToken ct)
    {
        var argumentos = texto.Split(' ', 2) is [_, var resto] ? resto : string.Empty;
        if (!ChavePixParser.TentarExtrair(argumentos, out var chaveNormalizada, out var erro))
            return erro;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grupo = await ObterGrupoAsync(db, chatId, ct);
        if (grupo is null)
            return "Ainda não tem ninguém na resenha — manda /entrar primeiro.";

        var participante = await db.Participantes.FirstOrDefaultAsync(p => p.GrupoId == grupo.Id && p.TelegramUserId == from.Id, ct);
        if (participante is null)
            return $"{NomeDe(from)}, manda /entrar primeiro antes de cadastrar a chave Pix.";

        var temNomeCompleto = await pagamentoService.SalvarChavePixAsync(participante.Id, chaveNormalizada, ct);
        if (!temNomeCompleto)
        {
            solicitacoesPendentes.RegistrarNome(from.Id, participante.Id);
            return "Chave Pix salva! Agora me manda seu nome completo no privado (abre uma conversa comigo) — é o que vai aparecer pro pagador no Pix.";
        }

        return await pagamentoService.GerarCobrancasPendentesAsync(participante.Id, ct);
    }

    private async Task TratarCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        var dados = callbackQuery.Data?.Split(':');
        if (dados is not ["pix", var acao, var idTexto] || !int.TryParse(idTexto, out var cobrancaId))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cobranca = await db.Cobrancas.Include(c => c.Credor).FirstOrDefaultAsync(c => c.Id == cobrancaId, ct);
        if (cobranca is null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Essa cobrança não existe mais.", cancellationToken: ct);
            return;
        }

        string texto;
        switch (acao)
        {
            case "cancelar":
                if (cobranca.Status is StatusCobranca.Pendente)
                {
                    cobranca.Status = StatusCobranca.Cancelada;
                    await db.SaveChangesAsync(ct);
                }
                texto = "Cobrança cancelada.";
                break;

            case "paguei":
                if (cobranca.Status == StatusCobranca.Paga)
                {
                    texto = "Já tava marcado como pago.";
                }
                else if (cobranca.Status == StatusCobranca.Pendente)
                {
                    cobranca.Status = StatusCobranca.Paga;
                    cobranca.PagoEm = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    texto = "Beleza, marquei como pago!";
                }
                else
                {
                    texto = "Essa cobrança não tá mais pendente.";
                }
                break;

            default:
                texto = "Ação desconhecida.";
                break;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, texto, showAlert: true, cancellationToken: ct);
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

        var linhas = despesas.Select(d =>
        {
            var valorFormatado = d.MoedaOriginal is not null && d.ValorOriginal is not null
                ? $"{FormatarValorEstrangeiro(d.ValorOriginal.Value, d.MoedaOriginal)} ({d.Valor:C})"
                : d.Valor.ToString("C");

            return $"{d.CriadoEm:dd/MM} — {d.PagoPor?.Nome} pagou {valorFormatado} ({d.Descricao})";
        });

        return "Últimas despesas:\n" + string.Join('\n', linhas);
    }

    private async Task<string?> ExtratoCSVAsync(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var grupo = await ObterGrupoAsync(db, chatId, ct);
            if (grupo is null)
                return "Ainda não tem despesa nenhuma — manda /entrar e depois /despesa.";

            var despesas = await db.Despesas
                .Where(d => d.GrupoId == grupo.Id)
                .Include(d => d.PagoPor)
                .OrderBy(d => d.CriadoEm)
                .ToListAsync(ct);

            if (despesas.Count == 0)
                return "Nenhuma despesa lançada ainda.";

            var csvStream = exportadorCsv.GerarCsvDespesas(despesas);
            csvStream.Seek(0, SeekOrigin.Begin);

            await botClient.SendDocument(
                chatId,
                new InputFileStream(csvStream, $"extrato_{grupo.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"),
                caption: "Extrato de todas as despesas do grupo",
                cancellationToken: ct);

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar CSV para chat {ChatId}", chatId);
            return "Deu ruim gerando o arquivo CSV. Tenta de novo?";
        }
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

    // Extrai valor, moeda (null = BRL, sem conversão) e descrição de "/despesa <valor>
    // <descrição>" — aceita também o formato invertido ("/despesa <descrição> <valor>"),
    // erro de digitação comum.
    private static bool TentarExtrairDespesa(string texto, out decimal valor, out string? moeda, out string descricao)
    {
        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return DespesaTextoParser.TentarExtrair(partes, indiceInicial: 1, out valor, out moeda, out descricao);
    }

    private static string NomeDaMoeda(string codigo) => codigo switch
    {
        "USD" => "Dólar",
        "EUR" => "Euro",
        "GBP" => "Libra",
        _ => codigo,
    };

    private static string FormatarValorEstrangeiro(decimal valor, string moeda)
    {
        var simbolo = moeda switch { "USD" => "US$", "EUR" => "€", "GBP" => "£", _ => moeda + " " };
        return $"{simbolo}{valor:N2}";
    }

    // Mesma extração de valor/descrição (aceitando os dois formatos, oficial e invertido),
    // mas pra texto livre sem o "/despesa" na frente — usado pelo fluxo de /editar. "moeda"
    // é devolvida só porque a assinatura é compartilhada; quem chama a ignora de propósito
    // (edição sempre volta a ser BRL puro).
    private static bool TentarExtrairValorEDescricaoLivre(string texto, out decimal valor, out string? moeda, out string descricao)
    {
        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return DespesaTextoParser.TentarExtrair(partes, indiceInicial: 0, out valor, out moeda, out descricao);
    }
}
