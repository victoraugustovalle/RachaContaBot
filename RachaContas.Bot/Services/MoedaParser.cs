namespace RachaContas.Bot.Services;

/// <summary>
/// Detecta moeda estrangeira num valor digitado: "$50", "US$ 50", "€30",
/// "£20" (símbolo colado ou separado, sempre antes do número) ou "50 USD"
/// (código de 3 letras depois do número). "R$" e valores sem símbolo
/// nenhum são tratados como BRL (moeda = null — sem conversão).
/// </summary>
public static class MoedaParser
{
    // Símbolos verificados na ordem em que aparecem — StartsWith não é ambíguo
    // aqui porque cada um é um prefixo distinto (não há colisão entre eles).
    private static readonly (string Simbolo, string? Moeda)[] SimbolosConhecidos =
    [
        ("US$", "USD"),
        ("U$", "USD"),
        ("$", "USD"),
        ("€", "EUR"),
        ("£", "GBP"),
        ("R$", null),
    ];

    // Moedas que a api.frankfurter.dev aceita — usado só pra validar um código
    // de 3 letras explícito tipo "50 CAD" (BRL fica de fora: já é o padrão).
    private static readonly HashSet<string> CodigosValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUD", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR",
        "ISK", "JPY", "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "USD", "ZAR",
    };

    /// <returns>
    /// true se um valor foi reconhecido a partir de partes[indiceInicial]. "moeda" vem null
    /// pra BRL. "proximoIndice" é onde a descrição começa dali em diante.
    /// </returns>
    public static bool TentarExtrair(IReadOnlyList<string> partes, int indiceInicial, out decimal valor, out string? moeda, out int proximoIndice)
    {
        valor = 0;
        moeda = null;
        proximoIndice = indiceInicial;

        if (indiceInicial >= partes.Count)
            return false;

        var token = partes[indiceInicial];

        foreach (var (simbolo, moedaDoSimbolo) in SimbolosConhecidos)
        {
            if (token.Equals(simbolo, StringComparison.OrdinalIgnoreCase))
                return TentarValorSeparado(partes, indiceInicial + 1, moedaDoSimbolo, out valor, out moeda, out proximoIndice);

            if (token.StartsWith(simbolo, StringComparison.OrdinalIgnoreCase))
            {
                moeda = moedaDoSimbolo;
                proximoIndice = indiceInicial + 1;
                return ValorParser.TentarParsear(token[simbolo.Length..], out valor);
            }
        }

        // Sem símbolo: número puro. Olha se o token seguinte é um código de moeda explícito ("50 USD").
        if (!ValorParser.TentarParsear(token, out valor))
            return false;

        if (indiceInicial + 1 < partes.Count && CodigosValidos.Contains(partes[indiceInicial + 1]))
        {
            moeda = partes[indiceInicial + 1].ToUpperInvariant();
            proximoIndice = indiceInicial + 2;
        }
        else
        {
            proximoIndice = indiceInicial + 1;
        }

        return true;
    }

    private static bool TentarValorSeparado(
        IReadOnlyList<string> partes, int indiceValor, string? moedaDetectada,
        out decimal valor, out string? moeda, out int proximoIndice)
    {
        valor = 0;
        moeda = moedaDetectada;
        proximoIndice = indiceValor + 1;

        return indiceValor < partes.Count && ValorParser.TentarParsear(partes[indiceValor], out valor);
    }
}
