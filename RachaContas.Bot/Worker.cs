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

        // GetMe só serve pra logar o @username — é informativo, não essencial.
        // Uma soneca de rede aqui não pode derrubar o processo inteiro, já que
        // o polling de verdade (StartReceiving) já foi iniciado acima.
        try
        {
            var me = await botClient.GetMe(stoppingToken);
            logger.LogInformation("Bot @{Username} conectado e ouvindo mensagens.", me.Username);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Não consegui confirmar a conexão com o Telegram (GetMe falhou), mas o bot deve continuar ouvindo mensagens.");
        }

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
