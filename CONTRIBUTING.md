# Contribuindo com o RachaContas

Obrigado pelo interesse. Este documento existe pra deixar claro como o projeto é organizado e o que esperar antes de abrir uma issue ou um PR — não é burocracia, é só pra alinhar expectativa e evitar retrabalho.

## Rodando localmente

Pré-requisito: SDK do .NET 10.

```bash
git clone https://github.com/victoraugusto3215/RachaContaBot.git
cd RachaContaBot/RachaContas.Bot
dotnet run
```

Sem token do Telegram configurado, o processo sobe normalmente e só loga um aviso — dá pra mexer no banco e nos testes sem configurar nada do Telegram primeiro. Instruções completas de token estão no [README](./README.md#como-executar).

## Rodando os testes

```bash
dotnet test
```

São 67 testes hoje, todos sem depender de rede ou de um bot real conectado — se um teste seu precisar de internet ou de token, provavelmente está testando a coisa errada (veja "Onde colocar lógica nova" abaixo).

## Onde colocar lógica nova

O projeto segue uma separação deliberada, e ela existe pra manter os testes simples:

- **`Services/`** — regra de negócio pura. `DebtSimplifier`, `PixBrCodeGenerator`, `ValorParser` etc. não tocam em banco nem em Telegram, e são `static` quando fazem sentido. Se você está adicionando uma regra de negócio nova, ela entra aqui, e o teste dela não deveria precisar mockar nada.
- **`Bot/UpdateHandler`** — só tradução entre "mensagem do Telegram" e "chamada num Service". Não deveria crescer com lógica de negócio.
- **`Models/`** — entidades do EF Core, sem lógica.
- **`Data/`** — `AppDbContext` e mapeamento fino que a convenção do EF não resolve sozinha.

Se sua mudança não se encaixa claramente em um desses lugares, abra uma issue descrevendo o problema antes de escrever código — é mais fácil discutir a abordagem antes do que depois de um PR grande pronto.

## Testes pra código novo

- Lógica pura (parsing, cálculo, geração de payload) → teste unitário direto, sem mock, seguindo o padrão de `DebtSimplifierTests.cs` ou `ValorParserTests.cs`.
- Qualquer coisa que precise do banco → `SQLite em memória`, seguindo `SaldoCalculatorTests.cs`. Não precisa (e não deve) subir um banco real.
- Nada que chame a API do Telegram ou do Frankfurter de verdade nos testes — `CambioServiceTests.cs` mostra como testar a extração de cotação a partir de um JSON de exemplo, sem rede.

## Pull Requests

- PRs pequenos e focados em uma coisa só são mais fáceis de revisar e de mergear rápido.
- Descreva o **porquê**, não só o **o quê** — o código já mostra o que mudou; a motivação é o que ajuda a decidir se faz sentido.
- Mudança de arquitetura ou de comportamento visível (novo comando, mudança na forma de dividir despesa, etc.) — abra uma issue antes pra alinhar, principalmente se for algo grande.
- CI (build + testes) roda automaticamente em todo PR — se o selo estiver vermelho, dá uma olhada antes de pedir review.

## Reportando um bug

Diga o comando usado, o que esperava acontecer e o que aconteceu de fato. Se envolver valor monetário ou Pix, o formato exato do valor digitado ajuda bastante (o parser de valor tem bastante caso de borda documentado nos testes).

## Código de conduta

Sem drama: seja direto, foque no problema técnico, trate quem está contribuindo como faria com um colega.
