namespace RachaContas.Bot.Models;

public class Despesa
{
    public int Id { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    public int PagoPorParticipanteId { get; set; }
    public Participante? PagoPor { get; set; }

    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Quem divide essa despesa — snapshot no momento do lançamento, não muda se alguém entrar no grupo depois.</summary>
    public List<DespesaParticipante> Participantes { get; set; } = [];
}
