# Política de Segurança

## Escopo

O RachaContaBot lida com dados financeiros sensíveis dos usuários — valores
de despesas, saldos entre participantes e, quando o comando `/pagar` é
usado, chaves Pix cadastradas por cada pessoa. Levamos falhas nessas áreas a
sério, mesmo sendo um projeto mantido por uma única pessoa.

## Versões suportadas

Este é um bot com um único ambiente de produção. Apenas o código na branch
`master` recebe correções de segurança — não há versões antigas mantidas em
paralelo.

| Versão              | Suportada          |
| ------------------- | ------------------ |
| `master` (mais recente) | :white_check_mark: |
| Commits anteriores  | :x:                 |

## Reportando uma vulnerabilidade

**Não abra uma issue pública para vulnerabilidades de segurança.**

Em vez disso, reporte diretamente para **victoraugusto3215@gmail.com** com:

- Descrição do problema e impacto potencial (ex.: vazamento de chave Pix,
  acesso a despesas de outro grupo, bypass de confirmação de pagamento).
- Passos para reproduzir, se possível.
- Versão/commit afetado.

Você deve receber uma resposta inicial em até 72 horas. Se a vulnerabilidade
for confirmada, o objetivo é publicar uma correção antes de qualquer
divulgação pública, e você será creditado na descrição do fix (a menos que
prefira anonimato).

## Fora de escopo

- Este bot não processa pagamentos diretamente — o Pix gerado é um payload
  BR Code (copia e cola / QR code) lido pelo aplicativo do próprio banco do
  usuário; o RachaContaBot nunca tem custódia de dinheiro.
- Engenharia social contra usuários do Telegram está fora do escopo deste
  repositório.
