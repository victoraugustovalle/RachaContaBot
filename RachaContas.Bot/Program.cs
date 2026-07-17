using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RachaContas.Bot;
using RachaContas.Bot.Bot;
using RachaContas.Bot.Data;
using RachaContas.Bot.Services;
using Telegram.Bot;

// Sem isso, o {valor:C} usado nas respostas do bot depende da cultura do SO. No
// Windows local isso dá "R$" de graça, mas no container do fly.io não tem locale
// configurado e o .NET cai pra cultura invariante, cujo símbolo de moeda é "¤".
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("pt-BR");

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

var telegramToken = builder.Configuration["Telegram:Token"];
if (!string.IsNullOrWhiteSpace(telegramToken))
    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));

builder.Services.AddSingleton<SaldoCalculator>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CambioService>();
builder.Services.AddSingleton<TelegramNotificacaoService>();
builder.Services.AddSingleton<SolicitacoesChavePixPendentes>();
builder.Services.AddSingleton<PagamentoService>();
builder.Services.AddSingleton<UpdateHandler>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

host.Run();
