using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace RachaContas.Bot.Services;

/// <summary>
/// Cotação do dia via api.frankfurter.dev — gratuita, sem chave, sem cadastro.
/// Cache em memória por (moeda, dia): a cotação só muda uma vez por dia, não
/// faz sentido bater na API de novo a cada despesa lançada no mesmo dia.
/// </summary>
public class CambioService(IHttpClientFactory httpClientFactory, ILogger<CambioService> logger)
{
    private readonly ConcurrentDictionary<(string Moeda, DateOnly Dia), decimal> _cache = new();

    /// <returns>Quantos reais vale 1 unidade da moeda informada hoje, ou null se a busca falhar.</returns>
    public async Task<decimal?> ObterTaxaParaBrlAsync(string moeda, CancellationToken ct)
    {
        var chave = (Moeda: moeda.ToUpperInvariant(), Dia: DateOnly.FromDateTime(DateTime.UtcNow));
        if (_cache.TryGetValue(chave, out var cacheada))
            return cacheada;

        try
        {
            var client = httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(
                $"https://api.frankfurter.dev/v1/latest?base={chave.Moeda}&symbols=BRL", ct);

            var taxa = ExtrairTaxaBRL(json);
            if (taxa is { } valor)
                _cache[chave] = valor;

            return taxa;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha buscando cotação de {Moeda} pra BRL.", moeda);
            return null;
        }
    }

    /// <summary>Extrai rates.BRL do JSON da Frankfurter — isolado do HTTP pra dar pra testar sem rede.</summary>
    public static decimal? ExtrairTaxaBRL(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("rates", out var rates) && rates.TryGetProperty("BRL", out var brl))
                return brl.GetDecimal();
        }
        catch (JsonException)
        {
            // payload inesperado — quem chama trata null como "não consegui converter agora".
        }

        return null;
    }
}
