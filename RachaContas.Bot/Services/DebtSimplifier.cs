namespace RachaContas.Bot.Services;

/// <summary>Saldo líquido de um participante: positivo = tem a receber, negativo = deve.</summary>
public record SaldoParticipante(string Nome, decimal Valor);

public record Transferencia(string De, string Para, decimal Valor);

/// <summary>
/// Algoritmo guloso de simplificação de dívidas: casa repetidamente o maior
/// credor com o maior devedor até zerar todo mundo. É a abordagem prática
/// usada por apps como o Splitwise — não é garantidamente o número ótimo
/// exato de transferências (esse problema é NP-difícil em geral), mas produz
/// um resultado bom e é simples de entender e testar.
/// </summary>
public static class DebtSimplifier
{
    private const decimal Tolerancia = 0.01m;

    public static IReadOnlyList<Transferencia> Simplificar(IEnumerable<SaldoParticipante> saldos)
    {
        var credores = new List<(string Nome, decimal Valor)>();
        var devedores = new List<(string Nome, decimal Valor)>();

        foreach (var s in saldos)
        {
            if (s.Valor > Tolerancia)
                credores.Add((s.Nome, s.Valor));
            else if (s.Valor < -Tolerancia)
                devedores.Add((s.Nome, -s.Valor)); // guarda como valor positivo — "quanto essa pessoa deve"
        }

        var transferencias = new List<Transferencia>();

        while (credores.Count > 0 && devedores.Count > 0)
        {
            credores.Sort((a, b) => b.Valor.CompareTo(a.Valor));
            devedores.Sort((a, b) => b.Valor.CompareTo(a.Valor));

            var credor = credores[0];
            var devedor = devedores[0];
            var valor = Math.Min(credor.Valor, devedor.Valor);

            transferencias.Add(new Transferencia(devedor.Nome, credor.Nome, Math.Round(valor, 2)));

            var credorRestante = credor.Valor - valor;
            var devedorRestante = devedor.Valor - valor;

            if (credorRestante <= Tolerancia)
                credores.RemoveAt(0);
            else
                credores[0] = (credor.Nome, credorRestante);

            if (devedorRestante <= Tolerancia)
                devedores.RemoveAt(0);
            else
                devedores[0] = (devedor.Nome, devedorRestante);
        }

        return transferencias;
    }
}
