using System.Collections.Concurrent;

namespace RachaContas.Bot.Services;

// Rastreia quem ainda deve mandar chave Pix/nome completo no privado, entre o /pagar
// e a resposta chegando. Chaveado por TelegramUserId (a resposta vem num chat diferente
// de onde o /pagar rolou). Chave e nome em dicionários separados: são etapas sequenciais,
// nunca as duas pendentes ao mesmo tempo. Limitação aceita: pendência em 2 grupos ao
// mesmo tempo, só a mais recente vale.
public class SolicitacoesChavePixPendentes
{
    private readonly ConcurrentDictionary<long, int> _pendentesChave = new();
    private readonly ConcurrentDictionary<long, int> _pendentesNome = new();

    public void RegistrarChave(long telegramUserId, int participanteId) => _pendentesChave[telegramUserId] = participanteId;

    public bool TryConsumirChave(long telegramUserId, out int participanteId) => _pendentesChave.TryRemove(telegramUserId, out participanteId);

    /// <summary>Usado quando a resposta veio num formato inválido — a pessoa continua "devendo" a chave, pra poder tentar de novo.</summary>
    public void DevolverChave(long telegramUserId, int participanteId) => RegistrarChave(telegramUserId, participanteId);

    public void RegistrarNome(long telegramUserId, int participanteId) => _pendentesNome[telegramUserId] = participanteId;

    public bool TryConsumirNome(long telegramUserId, out int participanteId) => _pendentesNome.TryRemove(telegramUserId, out participanteId);

    /// <summary>Usado quando a resposta veio vazia — a pessoa continua "devendo" o nome, pra poder tentar de novo.</summary>
    public void DevolverNome(long telegramUserId, int participanteId) => RegistrarNome(telegramUserId, participanteId);
}
