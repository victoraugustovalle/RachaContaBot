using System.Globalization;
using System.Text;

namespace RachaContas.Bot.Services;

/// <summary>
/// Monta o payload "Pix copia-e-cola" (BR Code) — o formato EMV que o Banco
/// Central define pro Pix. É só texto com campos TLV (id + tamanho + valor)
/// mais um checksum CRC16 no final; qualquer banco reconhece isso sem precisar
/// de gateway, API ou conta em lugar nenhum — só a chave Pix de quem recebe.
/// </summary>
public static class PixBrCodeGenerator
{
    private const string CidadePadrao = "SAO PAULO";

    /// <summary>Monta o payload completo (com o CRC16 já calculado no final).</summary>
    public static string GerarPayload(string chavePix, string nomeRecebedor, decimal valor, string? txId, string cidade = CidadePadrao)
    {
        var nome = Sanitizar(nomeRecebedor, 25);
        var cidadeSanitizada = Sanitizar(cidade, 15);
        var referencia = SanitizarTxId(txId);

        // Maiúsculo de propósito: um Pix copia-e-cola de verdade gerado por banco
        // (conferido manualmente) usa "BR.GOV.BCB.PIX" — o CRC não liga pra
        // maiúscula/minúscula, mas o leitor real do banco parece comparar essa
        // string de forma sensível a caixa pra reconhecer o "arranjo" Pix.
        var merchantAccountInfo = Tlv("00", "BR.GOV.BCB.PIX") + Tlv("01", chavePix);
        var additionalData = Tlv("05", referencia);

        var semCrc =
            Tlv("00", "01") +                                   // Payload Format Indicator
            // Point of Initiation Method (01) fica de fora de propósito — é opcional pra
            // Pix estático, e vários geradores reais de referência simplesmente omitem
            // esse campo em vez de declarar "11" explicitamente.
            Tlv("26", merchantAccountInfo) +                    // Merchant Account Information — Pix
            Tlv("52", "0000") +                                 // Merchant Category Code
            Tlv("53", "986") +                                  // Transaction Currency (986 = BRL)
            Tlv("54", valor.ToString("F2", CultureInfo.InvariantCulture)) + // Transaction Amount
            Tlv("58", "BR") +                                   // Country Code
            Tlv("59", nome) +                                   // Merchant Name
            Tlv("60", cidadeSanitizada) +                       // Merchant City
            Tlv("62", additionalData) +                         // Additional Data Field Template
            "6304";                                              // Id + tamanho do CRC — o valor vem depois

        return semCrc + CalcularCrc16(semCrc);
    }

    private static string Tlv(string id, string valor)
    {
        if (valor.Length > 99)
            throw new ArgumentException($"Valor do campo {id} excede 99 caracteres (tem {valor.Length}) — violaria o formato de tamanho de 2 dígitos do EMV.");

        return $"{id}{valor.Length:D2}{valor}";
    }

    /// <summary>Maiúsculo, sem acento, só ASCII imprimível — o padrão EMV não garante suporte a acentuação.</summary>
    private static string Sanitizar(string texto, int tamanhoMaximo)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var semAcento = new StringBuilder();
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128)
                semAcento.Append(c);
        }

        var resultado = semAcento.ToString().ToUpperInvariant().Trim();
        return resultado.Length > tamanhoMaximo ? resultado[..tamanhoMaximo] : resultado;
    }

    /// <summary>Referência da transação — só alfanumérico, "***" quando não tem uma específica (convenção do padrão).</summary>
    private static string SanitizarTxId(string? txId)
    {
        if (string.IsNullOrWhiteSpace(txId))
            return "***";

        var alfanumerico = new string(txId.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray());
        if (alfanumerico.Length == 0)
            return "***";

        return alfanumerico.Length > 25 ? alfanumerico[..25] : alfanumerico;
    }

    /// <summary>
    /// CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF) — o algoritmo que o manual do Pix exige.
    /// Público só pra dar pra testar contra o vetor de teste padrão do algoritmo ("123456789" -> 0x29B1).
    /// </summary>
    public static string CalcularCrc16(string payload)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.ASCII.GetBytes(payload))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc.ToString("X4");
    }
}
