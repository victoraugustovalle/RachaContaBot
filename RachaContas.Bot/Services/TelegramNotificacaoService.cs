using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RachaContas.Bot.Services;

// Centraliza o "manda no privado, se não der cai pro grupo" — regra do Telegram:
// só dá pra mandar mensagem privada pra quem já deu /start no bot antes.
public class TelegramNotificacaoService(ITelegramBotClient? botClient, ILogger<TelegramNotificacaoService> logger)
{
    public async Task<bool> EnviarTextoPrivadoAsync(
        long telegramUserId, string mensagem, InlineKeyboardMarkup? teclado = null, ParseMode parseMode = ParseMode.None, CancellationToken ct = default)
    {
        if (botClient is null)
            return false;

        try
        {
            await botClient.SendMessage(telegramUserId, mensagem, parseMode: parseMode, replyMarkup: teclado, cancellationToken: ct);
            return true;
        }
        catch (ApiRequestException ex)
        {
            logger.LogInformation("Não consegui mandar mensagem privada pro usuário {UserId}: {Mensagem}", telegramUserId, ex.Message);
            return false;
        }
    }

    public async Task EnviarTextoComFallbackAsync(
        long telegramUserId, long chatIdFallback, string mensagemPrivada, string mensagemFallback,
        InlineKeyboardMarkup? teclado = null, ParseMode parseMode = ParseMode.None, CancellationToken ct = default)
    {
        if (await EnviarTextoPrivadoAsync(telegramUserId, mensagemPrivada, teclado, parseMode, ct))
            return;

        if (botClient is not null)
            await botClient.SendMessage(chatIdFallback, mensagemFallback, parseMode: parseMode, replyMarkup: teclado, cancellationToken: ct);
    }

    public async Task EnviarFotoComFallbackAsync(
        long telegramUserId, long chatIdFallback, byte[] fotoBytes, string legenda, string mensagemFallback,
        InlineKeyboardMarkup? teclado = null, CancellationToken ct = default)
    {
        if (botClient is null)
            return;

        try
        {
            using var stream = new MemoryStream(fotoBytes);
            await botClient.SendPhoto(telegramUserId, InputFile.FromStream(stream), caption: legenda, replyMarkup: teclado, cancellationToken: ct);
            return;
        }
        catch (ApiRequestException ex)
        {
            logger.LogInformation("Não consegui mandar a foto privada pro usuário {UserId}: {Mensagem}", telegramUserId, ex.Message);
        }

        using var streamFallback = new MemoryStream(fotoBytes);
        await botClient.SendPhoto(chatIdFallback, InputFile.FromStream(streamFallback), caption: mensagemFallback, replyMarkup: teclado, cancellationToken: ct);
    }
}
