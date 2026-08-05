# RachaContas

[![CI](https://github.com/victoraugustovalle/RachaContaBot/actions/workflows/ci.yml/badge.svg)](https://github.com/victoraugustovalle/RachaContaBot/actions/workflows/ci.yml)

![RachaContas — bot de Telegram para dividir despesas em grupo](docs/banner.jpeg)

Bot de Telegram que divide despesas de um grupo (viagem, república, happy hour) e calcula o menor número de transferências pra fechar a conta, o mesmo tipo de simplificação de dívidas que o Splitwise faz.

## Sobre

A ideia surgiu de um problema chato e recorrente: depois de uma viagem em grupo, cada um paga um pedaço das coisas e sobra alguém com a planilha na mão tentando descobrir quem deve quanto pra quem. O RachaContas resolve isso dentro do próprio Telegram, não precisa instalar app, criar conta ou convencer ninguém a usar outra ferramenta, só adicionar o bot no grupo que já existe.

O fluxo é simples: cada participante manda `/entrar`, quem paga alguma coisa lança com `/despesa`, e quando a resenha termina alguém manda `/fechar` pra saber quem paga quanto pra quem, com o menor número de transferências possível, não uma transferência por despesa. Se quiser, dá pra ir além e gerar os Pix de cada transferência direto no chat, sem sair do Telegram.

## Funcionalidades

**Grupo e participantes**
| Comando | O que faz |
|---|---|
| `/entrar` | Registra quem mandou o comando como participante do grupo |
| `/entrar <nome>` | Adiciona alguém manualmente, pra quem não usa Telegram ou não vai mandar o comando. Entra nas divisões normalmente, mas não recebe nada do bot no privado (nem Pix) |
| `/cancelar` | Sai de qualquer fluxo em andamento (editar, excluir, limpar, confirmar moeda) sem alterar nada |

**Despesas**
| Comando | O que faz |
|---|---|
| `/despesa <valor> <descrição>` | Lança um gasto pago por quem mandou, dividido igual entre todos os participantes já registrados. Aceita o formato invertido (`/despesa Uber pro aeroporto 45,50`) e moeda estrangeira (`$50`, `US$ 50`, `€30`, `50 USD`), nesse caso o bot pede confirmação antes de converter e salvar |
| `/desfazer` | Apaga a última despesa lançada por quem mandou o comando |
| `/editar` | Mostra a lista numerada de despesas do grupo pra escolher uma e corrigir valor/descrição |
| `/excluir` | Mostra a lista numerada pra escolher uma despesa e remover |
| `/limpar` | Apaga todas as despesas do grupo (pede confirmação explícita antes) |
| `/extrato` | Lista as últimas despesas lançadas, com valor original + convertido quando foi moeda estrangeira |

**Fechamento e saldo**
| Comando | O que faz |
|---|---|
| `/saldo` | Mostra quem deve e quem tem a receber, e o status dos Pix se teve um `/pagar` recente |
| `/fechar` | Calcula as transferências mínimas pra zerar as contas do grupo |

**Pix (opcional)**
| Comando | O que faz |
|---|---|
| `/pagar` | Gera um QR code Pix de verdade pra cada transferência do último `/fechar`, a partir da chave Pix de quem recebe. Quem nunca usa esse comando não percebe nenhuma diferença no resto do bot |
| `/chavepix <tipo> <chave>` | Cadastra ou corrige a própria chave Pix (tipo: `telefone`, `cpf`, `email` ou `aleatoria`) |

Fora do escopo desta versão: divisão customizada por pessoa, por enquanto a divisão de cada despesa é sempre igual entre todos os participantes do grupo.

## Tecnologias

| Categoria | Tecnologia | Onde entra |
|---|---|---|
| Runtime | .NET 10 | Worker Service — processo único em segundo plano, sem servidor HTTP |
| Bot | Telegram.Bot 22.10.1 (SDK oficial) | Long-polling — não precisa de URL pública nem HTTPS |
| Banco | SQLite | Um arquivo `.db` local, sem servidor de banco separado |
| ORM | EF Core 10 (Sqlite + Design) | Modelos, migrations, `DbContext` |
| Câmbio | [api.frankfurter.dev](https://frankfurter.dev) | Cotação do dia pra conversão de moeda estrangeira, gratuita, sem chave, sem cadastro |
| QR code | QRCoder 1.8.0 | Renderiza o payload do Pix em PNG, localmente, sem chamar API nenhuma |
| Testes | xUnit + coverlet | 67 testes: algoritmo, parsing, integração com SQLite em memória, CRC16 do Pix |

## Arquitetura

O projeto é um Worker Service .NET — não sobe servidor HTTP, não expõe porta nenhuma. `Program.cs` monta o `Host`, registra os serviços via DI (a maioria como singleton) e aplica as migrations pendentes no banco antes de iniciar (`db.Database.Migrate()`). Depois disso, um único `BackgroundService` (`Worker.cs`) assume: abre o long-polling do Telegram e fica escutando até o processo ser encerrado.

Cada mensagem que chega passa pelo `UpdateHandler`, que roteia por comando (`switch` em cima do texto) e devolve uma string de resposta. Fluxos que exigem mais de uma mensagem — `/editar`, `/excluir`, `/limpar`, confirmação de moeda estrangeira, são controlados por um dicionário em memória (`ConcurrentDictionary<(ChatId, UserId), EstadoConversa>`) dentro do próprio `UpdateHandler`, que é singleton. Isso é suficiente pro escopo do bot: o estado só precisa sobreviver entre duas mensagens seguidas da mesma pessoa, e se o processo reiniciar no meio de um fluxo, o pior caso é a pessoa ter que mandar o comando de novo, não há perda de dado real, só de "estou no meio de uma ação".

A separação de responsabilidades segue essa lógica:

- **Models** — entidades do EF Core, sem lógica.
- **Data** — `AppDbContext` e o mapeamento fino que a convenção automática do EF não resolve sozinha (ex.: FKs de `Despesa.PagoPor` e `Cobranca.Devedor/Credor` exigem `DeleteBehavior.Restrict` explícito, senão o SQLite recusa o delete em cascata duplo).
- **Services** — regra de negócio pura, sem depender do Telegram. `DebtSimplifier` e `PixBrCodeGenerator`, por exemplo, são `static` e não tocam em banco nem em rede — dá pra testar sem mockar nada.
- **Bot** — só tradução entre "mensagem do Telegram" e "chamada nos services".

Essa separação existe basicamente pra permitir testar a lógica sem simular uma conversa inteira do Telegram, os testes de `SaldoCalculator`, por exemplo, batem direto num SQLite em memória, sem UpdateHandler no meio.

## Fluxo da aplicação

**Lançar uma despesa (`/despesa`):**

```
Telegram (long-polling)
  → Worker recebe o update e repassa pro UpdateHandler
  → DespesaTextoParser separa valor, moeda e descrição
     (aceita "<valor> <descrição>" e o formato invertido)
  → MoedaParser/ValorParser interpretam o valor (BRL ou moeda estrangeira,
     formato "34,22" ou "34.22", sem ambiguidade)
  → se for moeda estrangeira: guarda estado pendente e pede confirmação
     (/confirmar busca a cotação via CambioService, com cache por dia)
  → grava Despesa + snapshot de DespesaParticipante (quem divide, no
     momento do lançamento — não muda se alguém entrar depois)
  → responde no chat com o valor, quem pagou e a cota por pessoa
```

**Fechar a conta e gerar o Pix (`/fechar` → `/pagar`):**

```
/fechar
  → SaldoCalculator soma pago-menos-cota de cada participante em todas
     as despesas do grupo
  → DebtSimplifier casa repetidamente o maior credor com o maior devedor
     até zerar todo mundo (guloso, não é o ótimo global — ver seção abaixo)
  → responde com a lista de transferências

/pagar
  → PagamentoService recria as mesmas transferências, cria um Fechamento
     e uma Cobranca por transferência
  → se o credor não tem chave Pix/nome completo salvos, pede no privado
     e retoma sozinho quando a resposta chegar
  → PixBrCodeGenerator monta o payload BR Code (TLV + CRC16) e
     PixQrCodeImageService renderiza o PNG — tudo local
  → TelegramNotificacaoService manda o QR e o copia-e-cola no privado do
     devedor, caindo pro grupo se a pessoa nunca deu /start no bot
```

### O algoritmo de simplificação

`DebtSimplifier` recebe o saldo líquido de cada participante (o que pagou menos a própria cota) e casa repetidamente o maior credor com o maior devedor até zerar todo mundo. Não é garantido ser o número ótimo global de transferências — esse problema é NP-difícil em geral — mas é a mesma abordagem prática usada por apps como o Splitwise: boa aproximação, simples de entender e de testar.

### Moeda estrangeira

`Despesa.Valor` é sempre o valor em BRL — é o único campo que `SaldoCalculator` e `DebtSimplifier` enxergam. Quando `/despesa` detecta um símbolo (`$`, `€`, `£`) ou um código de 3 letras (USD, EUR, CAD...) diferente de BRL, o bot não salva na hora: pergunta a confirmação, busca a cotação do dia via Frankfurter (com cache em memória por moeda/dia — não faz sentido bater na API de novo pra cada despesa do mesmo dia) e só então converte, guardando também o valor original e a taxa usada pra exibir depois no `/extrato`. Editar uma despesa que tinha moeda estrangeira via `/editar` zera esses campos — o valor editado vira a nova verdade em BRL.

### Pix sem gateway

O "Pix copia-e-cola" gerado pelo `/pagar` é só texto: um payload no formato BR Code (padrão EMV que o Banco Central define pro Pix), com a chave de quem recebe, nome, valor e um checksum CRC16 no final. Não tem gateway, OAuth nem conta em nenhuma plataforma — qualquer banco lê esse payload. `PixBrCodeGenerator` monta o payload campo por campo (TLV: id + tamanho + valor) e calcula o CRC16/CCITT-FALSE manualmente; os testes validam isso contra o vetor de teste padrão do algoritmo e contra Pix reais (inclusive um copia-e-cola gerado pelo banco do próprio autor, usado pra debugar uma diferença de maiúscula/minúscula que um leitor de QR real levava em conta e o `CRC` não).

A confirmação de pagamento é manual — o botão "Já paguei" marca a cobrança como paga direto, sem confirmação automática. 

## Estrutura de pastas

```
RachaContas.Bot/
  Models/         Grupo, Participante (com ChavePix e NomeCompleto), Despesa,
                  DespesaParticipante (join table), Fechamento, Cobranca
  Data/           AppDbContext — mapeamento EF Core + SQLite
  Migrations/     Histórico de schema (initial → moeda estrangeira → Pix → nome completo)
  Services/       DebtSimplifier       algoritmo puro, sem banco nem rede
                  SaldoCalculator      lê o banco, soma pago-menos-cota
                  ValorParser          interpreta "34,22" / "1.450,90" / "33.33"
                  MoedaParser          detecta símbolo/código de moeda estrangeira
                  DespesaTextoParser   junta os dois pra parsear "/despesa ..."
                  ChavePixParser       normaliza chave Pix por tipo (DICT)
                  CambioService        cotação via Frankfurter, com cache por dia
                  PixBrCodeGenerator   monta o payload BR Code + CRC16
                  PixQrCodeImageService  renderiza o payload em PNG (QRCoder)
                  PagamentoService     orquestra o /pagar de ponta a ponta
                  TelegramNotificacaoService  DM com fallback pro grupo
                  SolicitacoesChavePixPendentes  rastreia quem falta responder
  Bot/            UpdateHandler — roteia comandos, fluxos conversacionais e botões inline
  Program.cs      DI, startup, aplica migrations pendentes
  Worker.cs       BackgroundService que sobe o long-polling
RachaContas.Tests/
  DebtSimplifierTests.cs        algoritmo isolado, dados sintéticos
  SaldoCalculatorTests.cs       integração real: SQLite em memória, banco → saldo
  ValorParserTests.cs
  MoedaParserTests.cs
  DespesaTextoParserTests.cs    parsing de /despesa, incluindo o formato invertido
  ChavePixParserTests.cs        normalização de telefone/CPF/e-mail/aleatória
  CambioServiceTests.cs         extração da cotação de um JSON de exemplo, sem rede
  PixBrCodeGeneratorTests.cs    CRC16 contra vetor padrão e Pix reais
```

## Como executar

Pré-requisito: SDK do .NET 10.

```bash
git clone <url-do-repositorio>
cd RachaContas/RachaContas.Bot
dotnet run
```

Sem token do Telegram configurado, o processo sobe normalmente e só loga um aviso avisando que o bot não vai conectar — não quebra. Isso é proposital: dá pra rodar e mexer no banco/nos testes sem precisar configurar nada do Telegram primeiro.

O SQLite (`rachacontas.db`) é criado e as migrations são aplicadas automaticamente no startup (`db.Database.Migrate()` em `Program.cs`) — não precisa rodar `dotnet ef database update` manualmente.

Pra conectar de verdade a um bot do Telegram:

1. Fala com o [@BotFather](https://t.me/BotFather), manda `/newbot`, segue as instruções. Ele devolve um token na hora, sem aprovação nem espera.
2. Configura o token via user-secrets (nunca vai pro git):
   ```bash
   cd RachaContas.Bot
   dotnet user-secrets set "Telegram:Token" "SEU_TOKEN_AQUI"
   ```
3. Roda `dotnet run` de novo. O bot conecta e já dá pra testar numa conversa privada (`/start`, `/entrar`, `/despesa ...`) antes de adicionar a um grupo de verdade.

## Testes

```bash
dotnet test
```

67 testes no total, sem depender de rede nem de um bot real conectado:

- `DebtSimplifier` — algoritmo isolado, com dados sintéticos.
- `SaldoCalculator` — integração de verdade contra SQLite em memória (banco → saldo).
- `ValorParser`, `MoedaParser`, `DespesaTextoParser`, `ChavePixParser` — parsing de texto, casos válidos e inválidos.
- `CambioService` — extração da cotação a partir de um JSON de exemplo, sem chamar a API.
- `PixBrCodeGenerator` — CRC16 contra o vetor de teste padrão do algoritmo e contra Pix copia-e-cola reais.

## Configuração

| Onde | O quê |
|---|---|
| `appsettings.json` | `ConnectionStrings:Default` — string de conexão do SQLite (`Data Source=rachacontas.db`) |
| user-secrets (`RachaContas.Bot`) | `Telegram:Token` — token do bot, obtido com o @BotFather. Nunca é commitado; sem ele o bot simplesmente não conecta |
| Nada além disso | Frankfurter (câmbio) é uma API pública sem chave; QRCoder roda 100% local; não há gateway de pagamento nem serviço externo de Pix |

`.gitignore` já exclui `bin/`, `obj/`, `*.db` e `appsettings.Local.json` — o arquivo do SQLite gerado localmente não deve ir pro repositório.

## Demonstração

Um churrasco de verdade, simulado com amigos num grupo de teste no Telegram — do primeiro `/entrar` ao último Pix confirmado, sem cortes.

<table>
<tr>
<td width="33%"><img src="docs/demo/demo-01.jpg" width="100%"><br><sub><b>1.</b> Participantes entram na resenha com <code>/entrar</code></sub></td>
<td width="33%"><img src="docs/demo/demo-02.jpg" width="100%"><br><sub><b>2.</b> Mais gente entra e as primeiras despesas são lançadas (<code>/despesa Picanha 6Kg 234,76</code>)</sub></td>
<td width="33%"><img src="docs/demo/demo-03.jpg" width="100%"><br><sub><b>3.</b> Despesa em formato livre (<code>cerveja R$124,88</code>) e a lista do <code>/editar</code></sub></td>
</tr>
<tr>
<td width="33%"><img src="docs/demo/demo-04.jpg" width="100%"><br><sub><b>4.</b> Corrigindo um valor com <code>/editar</code> e lançando uma despesa em dólar</sub></td>
<td width="33%"><img src="docs/demo/demo-05.jpg" width="100%"><br><sub><b>5.</b> Confirmação da cotação do dia e o <code>/extrato</code> com o histórico</sub></td>
<td width="33%"><img src="docs/demo/demo-06.jpg" width="100%"><br><sub><b>6.</b> <code>/fechar</code> calcula as transferências mínimas pra zerar a conta</sub></td>
</tr>
<tr>
<td width="33%"><img src="docs/demo/demo-07.jpg" width="100%"><br><sub><b>7.</b> <code>/pagar</code> gera o primeiro QR code Pix, de verdade</sub></td>
<td width="33%"><img src="docs/demo/demo-08.jpg" width="100%"><br><sub><b>8.</b> QR + copia-e-cola, com botão "Já paguei" pra confirmar</sub></td>
<td width="33%"><img src="docs/demo/demo-09.jpg" width="100%"><br><sub><b>9.</b> Um Pix gerado automaticamente pra cada devedor</sub></td>
</tr>
<tr>
<td width="33%"><img src="docs/demo/demo-10.jpg" width="100%"><br><sub><b>10.</b> Confirmação manual de pagamento, direto no chat</sub></td>
<td width="33%"><img src="docs/demo/demo-11.jpg" width="100%"><br><sub><b>11.</b> Os 9 Pix da resenha, todos gerados e enviados</sub></td>
<td width="33%"><img src="docs/demo/demo-12.jpg" width="100%"><br><sub><b>12.</b> <code>/saldo</code> final — quem já pagou e quem ainda falta</sub></td>
</tr>
</table>

---

Se o RachaContas te ajudou ou você curtiu a ideia, considera deixar uma ⭐ no repositório — não custa nada e ajuda o projeto a ser encontrado por mais gente.
