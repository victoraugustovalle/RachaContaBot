namespace RachaContas.Bot.Services;

/// <summary>
/// Normaliza uma chave Pix a partir de "&lt;tipo&gt; &lt;valor&gt;" digitado por alguém.
/// Existe porque o DICT (diretório de chaves do Banco Central) exige um formato
/// exato por tipo — ex.: telefone tem que vir como "+55DDDNUMERO", sem espaço,
/// parênteses ou traço. Sem isso, o BR Code até é válido tecnicamente, mas o
/// banco de quem for pagar não acha a chave e recusa como "QR code inválido".
/// </summary>
public static class ChavePixParser
{
    public static bool TentarExtrair(string texto, out string chaveNormalizada, out string erro)
    {
        chaveNormalizada = string.Empty;
        erro = string.Empty;

        var partes = texto.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 2)
        {
            erro = "Manda no formato: <tipo> <chave>. Tipo pode ser telefone, cpf, email ou aleatoria. Ex.: telefone 11999998888";
            return false;
        }

        var tipo = partes[0].ToLowerInvariant();
        var valor = partes[1].Trim();

        var normalizada = tipo switch
        {
            "telefone" or "celular" => NormalizarTelefone(valor),
            "cpf" => NormalizarCpf(valor),
            "email" or "e-mail" => valor.Length <= 77 ? valor.ToLowerInvariant() : null,
            "aleatoria" or "aleatória" or "evp" => valor.Length <= 77 ? valor.ToLowerInvariant() : null,
            _ => null,
        };

        if (normalizada is null)
        {
            erro = tipo switch
            {
                "telefone" or "celular" or "cpf" => $"Não reconheci esse {tipo} — confere se digitou os números certos (com ou sem DDD/pontuação, eu ajusto).",
                "email" or "e-mail" or "aleatoria" or "aleatória" or "evp" => "Essa chave é longa demais — o Pix não aceita mais que 77 caracteres nesse campo.",
                _ => $"Não conheço o tipo \"{tipo}\" — usa telefone, cpf, email ou aleatoria.",
            };
            return false;
        }

        chaveNormalizada = normalizada;
        return true;
    }

    /// <summary>DICT exige "+55" + DDD + número, sem espaço/parêntese/traço.</summary>
    private static string? NormalizarTelefone(string valor)
    {
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return digitos.Length switch
        {
            12 or 13 when digitos.StartsWith("55") => "+" + digitos,
            10 or 11 => "+55" + digitos,
            _ => null,
        };
    }

    private static string? NormalizarCpf(string valor)
    {
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return digitos.Length == 11 ? digitos : null;
    }
}
