using System.Text;
using RachaContas.Bot.Models;

namespace RachaContas.Bot.Services;

public class ExportadorCsv
{
    public MemoryStream GerarCsvDespesas(List<Despesa> despesas)
    {
        var csv = new StringBuilder();

        csv.AppendLine("Data,Descrição,Quem Pagou,Valor (BRL),Valor Original,Moeda Original");

        foreach (var despesa in despesas)
        {
            var data = despesa.CriadoEm.ToString("dd/MM/yyyy");
            var descricao = EscaparCampoCSV(despesa.Descricao);
            var pagador = EscaparCampoCSV(despesa.PagoPor?.Nome ?? "Desconhecido");
            var valor = despesa.Valor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var valorOriginal = despesa.ValorOriginal?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "";
            var moedaOriginal = despesa.MoedaOriginal ?? "";

            csv.AppendLine($"{data},{descricao},{pagador},{valor},{valorOriginal},{moedaOriginal}");
        }

        var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
        var bytes = encoding.GetBytes(csv.ToString());
        var stream = new MemoryStream(bytes);
        return stream;
    }

    private static string EscaparCampoCSV(string campo)
    {
        if (string.IsNullOrWhiteSpace(campo))
            return string.Empty;

        if (campo.Contains(',') || campo.Contains('"') || campo.Contains('\n'))
            return $"\"{campo.Replace("\"", "\"\"")}\"";

        return campo;
    }
}
