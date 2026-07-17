namespace RachaContas.Bot.Models;

public enum StatusCobranca
{
    Pendente,
    Paga,
    Cancelada,
}

/// <summary>Uma cobrança Pix por transferência de /fechar (devedor -> credor).</summary>
public class Cobranca
{
    public int Id { get; set; }

    public int FechamentoId { get; set; }
    public Fechamento? Fechamento { get; set; }

    public int DevedorParticipanteId { get; set; }
    public Participante? Devedor { get; set; }

    public int CredorParticipanteId { get; set; }
    public Participante? Credor { get; set; }

    public decimal Valor { get; set; }
    public StatusCobranca Status { get; set; } = StatusCobranca.Pendente;

    /// <summary>Payload BR Code (Pix copia-e-cola) — a imagem do QR é gerada na hora a partir dele, não fica salva.</summary>
    public string? PixCopiaECola { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? PagoEm { get; set; }
}
