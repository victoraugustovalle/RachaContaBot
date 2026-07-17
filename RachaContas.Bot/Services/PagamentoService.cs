using Microsoft.EntityFrameworkCore;
using RachaContas.Bot.Data;
using RachaContas.Bot.Models;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RachaContas.Bot.Services;

/// <summary>
/// Orquestra a geração das cobranças Pix de um /pagar: cria o Fechamento + uma
/// Cobranca por transferência e monta o BR Code (QR + copia-e-cola) na hora,
/// 100% local — sem gateway, sem API externa, só a chave Pix do credor. Se o
/// credor ainda não tem uma chave Pix salva, pede pra ele no privado e retoma
/// sozinho assim que a resposta chegar.
/// </summary>
public class PagamentoService(
    IDbContextFactory<AppDbContext> dbFactory,
    SaldoCalculator saldoCalculator,
    TelegramNotificacaoService notificacao,
    SolicitacoesChavePixPendentes solicitacoesPendentes)
{
    public async Task<string> ProcessarPagamentoAsync(int grupoId, long chatId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var saldos = await saldoCalculator.CalcularAsync(db, grupoId, ct);
        var transferencias = DebtSimplifier.Simplificar(saldos);
        if (transferencias.Count == 0)
            return "Já tá tudo quite — ninguém deve nada pra ninguém.";

        var participantesPorId = await db.Participantes.Where(p => p.GrupoId == grupoId).ToDictionaryAsync(p => p.Id, ct);

        var fechamento = new Fechamento { GrupoId = grupoId };
        db.Fechamentos.Add(fechamento);
        await db.SaveChangesAsync(ct);

        var cobrancas = transferencias.Select(t => new Cobranca
        {
            FechamentoId = fechamento.Id,
            DevedorParticipanteId = t.DevedorId,
            Devedor = participantesPorId[t.DevedorId],
            CredorParticipanteId = t.CredorId,
            Credor = participantesPorId[t.CredorId],
            Valor = t.Valor,
        }).ToList();
        db.Cobrancas.AddRange(cobrancas);
        await db.SaveChangesAsync(ct);

        var pixGerados = 0;
        var pedidosDeChave = 0;
        var pedidosDeNome = 0;

        foreach (var grupoDeCobrancas in cobrancas.GroupBy(c => c.CredorParticipanteId))
        {
            var credor = participantesPorId[grupoDeCobrancas.Key];
            var temChave = !string.IsNullOrWhiteSpace(credor.ChavePix);
            var temNome = !string.IsNullOrWhiteSpace(credor.NomeCompleto);

            if (temChave && temNome)
            {
                foreach (var cobranca in grupoDeCobrancas)
                {
                    await GerarEEnviarPixAsync(db, cobranca, chatId, ct);
                    pixGerados++;
                }
            }
            else if (!temChave)
            {
                solicitacoesPendentes.RegistrarChave(credor.TelegramUserId, credor.Id);
                await notificacao.EnviarTextoComFallbackAsync(
                    credor.TelegramUserId, chatId,
                    "Pra eu gerar o Pix pra você receber, manda sua chave Pix aqui no privado, no formato: <tipo> <chave>. " +
                    "Tipo pode ser telefone, cpf, email ou aleatoria. Ex.: telefone 11999998888",
                    $"{credor.Nome}, preciso da sua chave Pix — abre uma conversa privada comigo (clica no meu nome) e me manda ela por lá.",
                    ct: ct);
                pedidosDeChave++;
            }
            else
            {
                solicitacoesPendentes.RegistrarNome(credor.TelegramUserId, credor.Id);
                await notificacao.EnviarTextoComFallbackAsync(
                    credor.TelegramUserId, chatId,
                    "Pra eu gerar o Pix pra você receber, preciso do seu nome completo (é o que aparece pro pagador no Pix) — manda aqui no privado.",
                    $"{credor.Nome}, preciso do seu nome completo — abre uma conversa privada comigo (clica no meu nome) e me manda ele por lá.",
                    ct: ct);
                pedidosDeNome++;
            }
        }

        var partes = new List<string>();
        if (pixGerados > 0)
            partes.Add($"{pixGerados} Pix gerado(s) e enviado(s)");
        if (pedidosDeChave > 0)
            partes.Add($"pedi a chave Pix de {pedidosDeChave} pessoa(s) que ainda não tinham — assim que responderem eu gero o Pix sozinho");
        if (pedidosDeNome > 0)
            partes.Add($"pedi o nome completo de {pedidosDeNome} pessoa(s) que ainda faltava — assim que responderem eu gero o Pix sozinho");

        return "Beleza, comecei o processo de pagamento: " + string.Join(" — ", partes) + ".";
    }

    /// <summary>
    /// Chamado quando a resposta com a chave Pix chega no privado. Salva a chave e devolve se
    /// ainda falta pedir o nome completo — só depois de ter os dois é que as cobranças que
    /// estavam esperando são destravadas (o nome completo é obrigatório no BR Code).
    /// </summary>
    public async Task<bool> SalvarChavePixAsync(int credorParticipanteId, string chavePix, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var participante = await db.Participantes.FindAsync([credorParticipanteId], ct);
        if (participante is null)
            return false;

        participante.ChavePix = chavePix.Trim();
        await db.SaveChangesAsync(ct);

        return !string.IsNullOrWhiteSpace(participante.NomeCompleto);
    }

    /// <summary>Chamado quando a resposta com o nome completo chega no privado — salva o nome.</summary>
    public async Task SalvarNomeCompletoAsync(int participanteId, string nomeCompleto, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var participante = await db.Participantes.FindAsync([participanteId], ct);
        if (participante is null)
            return;

        participante.NomeCompleto = nomeCompleto.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Gera e envia os Pix que estavam esperando essa pessoa terminar de cadastrar chave + nome completo.</summary>
    public async Task<string> GerarCobrancasPendentesAsync(int credorParticipanteId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var pendentes = await db.Cobrancas
            .Where(c => c.CredorParticipanteId == credorParticipanteId && c.Status == StatusCobranca.Pendente && c.PixCopiaECola == null)
            .Include(c => c.Devedor)
            .Include(c => c.Credor)
            .Include(c => c.Fechamento!).ThenInclude(f => f.Grupo)
            .ToListAsync(ct);

        if (pendentes.Count == 0)
            return "Dados salvos! Da próxima vez que alguém mandar /pagar, já uso eles direto.";

        foreach (var cobranca in pendentes)
            await GerarEEnviarPixAsync(db, cobranca, cobranca.Fechamento!.Grupo!.TelegramChatId, ct);

        return $"Dados salvos! Gerei {pendentes.Count} Pix que estavam esperando por você.";
    }

    private async Task GerarEEnviarPixAsync(AppDbContext db, Cobranca cobranca, long chatIdGrupo, CancellationToken ct)
    {
        var nomeRecebedor = string.IsNullOrWhiteSpace(cobranca.Credor!.NomeCompleto) ? cobranca.Credor.Nome : cobranca.Credor.NomeCompleto;
        var payload = PixBrCodeGenerator.GerarPayload(cobranca.Credor.ChavePix!, nomeRecebedor, cobranca.Valor, $"racha{cobranca.Id}");
        cobranca.PixCopiaECola = payload;
        await db.SaveChangesAsync(ct);

        var teclado = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithCallbackData("Já paguei", $"pix:paguei:{cobranca.Id}"),
            InlineKeyboardButton.WithCallbackData("Cancelar", $"pix:cancelar:{cobranca.Id}"),
        });
        var aviso = $"Pix de {cobranca.Valor:C} pra {cobranca.Credor.Nome}.";

        // QR code e código separados de propósito — assim dá pra copiar o código
        // com um toque (bloco monoespaçado do Telegram), sem misturar com o resto.
        var qrPng = PixQrCodeImageService.GerarPng(payload);
        await notificacao.EnviarFotoComFallbackAsync(
            cobranca.Devedor!.TelegramUserId, chatIdGrupo, qrPng, aviso, $"{cobranca.Devedor.Nome}: {aviso}", ct: ct);

        var codigo = System.Net.WebUtility.HtmlEncode(payload);
        await notificacao.EnviarTextoComFallbackAsync(
            cobranca.Devedor.TelegramUserId, chatIdGrupo,
            $"Copia e cola:\n<code>{codigo}</code>", $"{cobranca.Devedor.Nome}, copia e cola:\n<code>{codigo}</code>",
            teclado, ParseMode.Html, ct);
    }
}
