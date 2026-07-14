namespace RachaContas.Bot.Models;

/// <summary>Join table: quem divide uma despesa específica.</summary>
public class DespesaParticipante
{
    public int DespesaId { get; set; }
    public Despesa? Despesa { get; set; }

    public int ParticipanteId { get; set; }
    public Participante? Participante { get; set; }
}
