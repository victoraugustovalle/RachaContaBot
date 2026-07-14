using RachaContas.Bot.Bot;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace RachaContas.Bot;

public class Worker(
    UpdateHandler updateHandler,
    IConfiguration config,
    ILogger<Worker> logger,
    ITelegramBotClient? botClient = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (botClient is null || string.IsNullOrWhiteSpace(config["Telegram:Token"]))
        {
            logger.LogWarning(
                "Telegram:Token não configurado — o bot não vai conectar. " +
                "Rode: dotnet user-secrets set \"Telegram:Token\" \"...\" (pega o token com o @BotFather).");
            return;
        }

        var receiverOptions = new ReceiverOptions { AllowedUpdates = [] };
        botClient.StartReceiving(updateHandler, receiverOptions, stoppingToken);

        var me = await botClient.GetMe(stoppingToken);
        logger.LogInformation("Bot @{Username} conectado e ouvindo mensagens.", me.Username);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Desligamento normal do host.
        }
    }
}
