namespace RachaContas.Bot.Models;

public class Grupo
{
    public int Id { get; set; }

    /// <summary>Id do chat do Telegram — cada grupo do Telegram vira um Grupo aqui.</summary>
    public long TelegramChatId { get; set; }

    public List<Participante> Participantes { get; set; } = [];
    public List<Despesa> Despesas { get; set; } = [];
}
