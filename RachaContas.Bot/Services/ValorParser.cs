using System.Globalization;

namespace RachaContas.Bot.Services;

/// <summary>
/// Interpreta valores digitados em formato brasileiro ("34,22", "1.450,90")
/// ou internacional ("33.33", "1,450.90") sem ambiguidade: o separador que
/// aparece por último no texto é sempre o decimal; o outro, se houver, é
/// milhar e é descartado.
/// </summary>
public static class ValorParser
{
    public static bool TentarParsear(string texto, out decimal valor)
    {
        var indicePonto = texto.LastIndexOf('.');
        var indiceVirgula = texto.LastIndexOf(',');

        string normalizado;
        if (indicePonto >= 0 && indiceVirgula >= 0)
        {
            normalizado = indiceVirgula > indicePonto
                ? texto.Replace(".", "").Replace(',', '.')  // "1.450,90" -> "1450.90"
                : texto.Replace(",", "");                    // "1,450.90" -> "1450.90"
        }
        else if (indiceVirgula >= 0)
        {
            normalizado = texto.Replace(',', '.');            // "34,22" -> "34.22"
        }
        else
        {
            normalizado = texto;                               // "33.33" ou "30" já estão ok
        }

        return decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }
}
