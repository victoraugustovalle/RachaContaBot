using Microsoft.EntityFrameworkCore;
using RachaContas.Bot.Data;

namespace RachaContas.Bot.Services;

/// <summary>
/// Calcula o saldo líquido de cada participante de um grupo (pago − cota em
/// cada despesa que ele divide). Usado tanto por /saldo quanto por /fechar.
/// Separado do UpdateHandler pra poder ser testado com um banco de verdade,
/// sem precisar simular uma conversa do Telegram.
/// </summary>
public class SaldoCalculator
{
    public async Task<List<SaldoParticipante>> CalcularAsync(AppDbContext db, int grupoId, CancellationToken ct = default)
    {
        var participantes = await db.Participantes.Where(p => p.GrupoId == grupoId).ToListAsync(ct);
        var despesas = await db.Despesas
            .Where(d => d.GrupoId == grupoId)
            .Include(d => d.Participantes)
            .ToListAsync(ct);

        var saldoPorId = participantes.ToDictionary(p => p.Id, _ => 0m);

        foreach (var despesa in despesas)
        {
            if (despesa.Participantes.Count == 0)
                continue;

            var cota = despesa.Valor / despesa.Participantes.Count;

            if (saldoPorId.ContainsKey(despesa.PagoPorParticipanteId))
                saldoPorId[despesa.PagoPorParticipanteId] += despesa.Valor;

            foreach (var dp in despesa.Participantes)
            {
                if (saldoPorId.ContainsKey(dp.ParticipanteId))
                    saldoPorId[dp.ParticipanteId] -= cota;
            }
        }

        return participantes.Select(p => new SaldoParticipante(p.Id, p.Nome, saldoPorId[p.Id])).ToList();
    }
}
