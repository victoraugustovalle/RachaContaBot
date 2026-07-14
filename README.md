# RachaContas

Bot de Telegram que divide despesas de grupo (viagem, república, happy hour) e calcula o **menor número de transferências** pra quitar todo mundo — o mesmo tipo de algoritmo de simplificação de dívidas que apps como o Splitwise usam.

Projeto de portfólio — conecta com algo que já existe no autor: vários repositórios de algoritmo/estrutura de dados (busca em grafos, árvores, crivo de primos) que até então eram exercícios soltos sem produto em volta. Aqui o algoritmo tem um uso real, e é a primeira parte do portfólio com testes automatizados de verdade.

## Stack

- **.NET 10** — Worker Service (processo único, roda em segundo plano)
- **Telegram.Bot 22** (SDK oficial) — long-polling, sem precisar de URL pública/HTTPS
- **EF Core + SQLite**
- **xUnit** — 9 testes: o algoritmo de simplificação isolado (dados sintéticos) e a integração completa banco → cálculo de saldo → simplificação (SQLite em memória)

## Comandos

- `/entrar` — registra quem mandou o comando como participante do grupo
- `/despesa <valor> <descrição>` — lança um gasto pago por quem mandou, dividido igual entre todos os participantes já registrados (ex.: `/despesa 45,50 Uber pro aeroporto`)
- `/saldo` — mostra quem deve e quem tem a receber
- `/fechar` — calcula as transferências mínimas pra zerar as contas do grupo
- `/extrato` — lista as últimas despesas lançadas

Fora do escopo desta versão: divisão customizada por pessoa (só igual, por enquanto), edição/exclusão de despesa, múltiplas moedas.

## O algoritmo

`DebtSimplifier` (em `RachaContas.Bot/Services/DebtSimplifier.cs`) recebe o saldo líquido de cada participante (o que pagou menos sua cota) e vai casando repetidamente o maior credor com o maior devedor até zerar todo mundo — abordagem gulosa, a mesma usada na prática por apps reais. **Nota honesta**: não é garantidamente o número ótimo global de transferências (esse problema é NP-difícil em geral) — é uma boa aproximação, simples de entender e de testar.

## Rodando localmente

```bash
cd RachaContas.Bot
dotnet run
```

Sem token do Telegram configurado, o processo sobe normalmente e só loga um aviso claro dizendo que o bot não vai conectar — não quebra.

## Configurando o bot (grátis, na hora)

1. Fala com o [@BotFather](https://t.me/BotFather) no Telegram, manda `/newbot`, segue as instruções. Ele te dá um token na hora — sem aprovação, sem espera (bem mais simples que o processo do Mercado Pago/Meta usado no outro projeto do portfólio).
2. Configura o token via user-secrets (nunca commitado):
   ```bash
   cd RachaContas.Bot
   dotnet user-secrets set "Telegram:Token" "SEU_TOKEN_AQUI"
   ```
3. Roda `dotnet run` de novo. O bot conecta e já dá pra testar — pode falar com ele em uma conversa privada primeiro (`/start`, `/entrar`, `/despesa ...`) antes de adicionar a um grupo de verdade.

## Estrutura

```
RachaContas.Bot/
  Models/       Grupo, Participante, Despesa, DespesaParticipante
  Data/         AppDbContext (EF Core + SQLite)
  Services/     DebtSimplifier (algoritmo puro), SaldoCalculator (lê o banco)
  Bot/          UpdateHandler (roteia os comandos do Telegram)
  Worker.cs     BackgroundService que inicia o long-polling
RachaContas.Tests/
  DebtSimplifierTests.cs     algoritmo isolado, dados sintéticos
  SaldoCalculatorTests.cs    integração real com SQLite em memória, banco -> saldo -> simplificação
```
