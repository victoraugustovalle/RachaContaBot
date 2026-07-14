namespace RachaContas.Bot.Services;

/// <summary>
/// Extrai valor + moeda + descrição do texto de uma despesa, aceitando tanto
/// o formato oficial ("&lt;valor&gt; &lt;descrição&gt;", ex.: "50 uber") quanto o
/// invertido ("&lt;descrição&gt; &lt;valor&gt;", ex.: "uber 50" ou "uber R$50") — troca
/// comum de quem tá acostumado a falar o motivo antes do valor.
/// </summary>
public static class DespesaTextoParser
{
    /// <param name="partes">Tokens já divididos por espaço, sem o "/despesa" na frente (ou com ele — ver <paramref name="indiceInicial"/>).</param>
    /// <param name="indiceInicial">Onde o valor OU a descrição começam (1 se partes[0] for "/despesa"; 0 pra texto livre).</param>
    public static bool TentarExtrair(IReadOnlyList<string> partes, int indiceInicial, out decimal valor, out string? moeda, out string descricao)
    {
        valor = 0;
        moeda = null;
        descricao = string.Empty;

        if (partes.Count < indiceInicial + 2)
            return false;

        // Formato oficial: valor logo no início.
        if (MoedaParser.TentarExtrair(partes, indiceInicial, out valor, out moeda, out var proximoIndice))
        {
            descricao = string.Join(' ', partes.Skip(proximoIndice));
            if (!string.IsNullOrWhiteSpace(descricao))
                return true;
        }

        // Formato invertido: valor no final ("uber 50", "uber R$50", "uber US$ 50", "uber 50 USD").
        // Testa os 2 últimos tokens juntos ANTES do último sozinho — senão "US$ 50" no fim
        // da frase seria lido errado (o "US$" ficaria preso na descrição e só o "50" viraria valor).
        if (partes.Count >= indiceInicial + 3
            && MoedaParser.TentarExtrair(partes, partes.Count - 2, out var valor2, out var moeda2, out var fim2)
            && fim2 == partes.Count)
        {
            descricao = string.Join(' ', partes.Skip(indiceInicial).Take(partes.Count - 2 - indiceInicial));
            if (!string.IsNullOrWhiteSpace(descricao))
            {
                valor = valor2;
                moeda = moeda2;
                return true;
            }
        }

        if (MoedaParser.TentarExtrair(partes, partes.Count - 1, out var valor1, out var moeda1, out var fim1)
            && fim1 == partes.Count)
        {
            descricao = string.Join(' ', partes.Skip(indiceInicial).Take(partes.Count - 1 - indiceInicial));
            if (!string.IsNullOrWhiteSpace(descricao))
            {
                valor = valor1;
                moeda = moeda1;
                return true;
            }
        }

        valor = 0;
        moeda = null;
        descricao = string.Empty;
        return false;
    }
}
