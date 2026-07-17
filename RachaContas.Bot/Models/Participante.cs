namespace RachaContas.Bot.Models;

public class Participante
{
    public int Id { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    /// <summary>Id do usuário no Telegram — único dentro do grupo (a mesma pessoa pode estar em vários grupos).</summary>
    public long TelegramUserId { get; set; }

    public string Nome { get; set; } = string.Empty;

    /// <summary>Chave Pix (CPF, e-mail, telefone ou aleatória) usada pra gerar os QR codes de /pagar quando essa pessoa é credora.</summary>
    public string? ChavePix { get; set; }

    /// <summary>
    /// Nome completo informado pela própria pessoa, usado como Merchant Name no BR Code
    /// gerado quando ela é credora. Separado de <see cref="Nome"/> (que vem do perfil do
    /// Telegram e pode ser só um apelido) porque o nome que aparece no Pix pro pagador
    /// devia ser um nome de verdade, não o que a pessoa colocou como nome de usuário.
    /// </summary>
    public string? NomeCompleto { get; set; }
}
