namespace RachaContas.Bot.Models;

public class Participante
{
    public int Id { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    /// <summary>Id do usuário no Telegram — único dentro do grupo (a mesma pessoa pode estar em vários grupos).</summary>
    public long TelegramUserId { get; set; }

    public string Nome { get; set; } = string.Empty;
}
