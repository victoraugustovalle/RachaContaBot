namespace RachaContas.Bot.Models;

/// <summary>Snapshot do momento em que alguém pediu pra gerar os Pix de /fechar (comando /pagar).</summary>
public class Fechamento
{
    public int Id { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<Cobranca> Cobrancas { get; set; } = [];
}
