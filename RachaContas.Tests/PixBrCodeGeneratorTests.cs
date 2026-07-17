using RachaContas.Bot.Services;
using Xunit;

namespace RachaContas.Tests;

public class PixBrCodeGeneratorTests
{
    [Fact]
    public void CalcularCrc16_VetorDeTestePadraoDoAlgoritmo_BateComOValorCatalogado()
    {
        // "123456789" -> 0x29B1 é o vetor de teste padrão pra CRC-16/CCITT-FALSE
        // (mesmo poly/init que o manual do Pix usa) — valida o algoritmo em si,
        // independente do payload específico do Pix.
        var crc = PixBrCodeGenerator.CalcularCrc16("123456789");

        Assert.Equal("29B1", crc);
    }

    [Fact]
    public void CalcularCrc16_ExemploRealDoRepositorioCharlesMendes_BateComOCrcPublicado()
    {
        // Exemplo curado de github.com/CharlesMendes/brcode (lista de QR Codes Pix
        // conhecidamente válidos, feita especificamente pra testar validadores) —
        // confirma o algoritmo contra um caso real, não só o vetor de teste abstrato.
        var semCrc =
            "00020126470014br.gov.bcb.pix0125contato@charlesmendes.com" +
            "52040000530398654040.075802BR5922CHARLES PEREIRA MENDES" +
            "6009SAO PAULO62070503***6304";

        var crc = PixBrCodeGenerator.CalcularCrc16(semCrc);

        Assert.Equal("DC64", crc);
    }

    [Fact]
    public void CalcularCrc16_ExemploRealGeradoPeloBancoDoVictor_BateComOCrcPublicado()
    {
        // Pix copia-e-cola de verdade, gerado pelo banco do Victor (conferido manualmente
        // durante o debug do "QR Code inválido") — usa GUI em maiúsculo (BR.GOV.BCB.PIX),
        // que é o que revelou a diferença real com o que a gente gerava antes.
        var semCrc =
            "00020126580014BR.GOV.BCB.PIX01361ade9854-82d1-04cd-8422-b4aa343d59f2" +
            "52040000530398654045.005802BR5925Maria Ferreira Silva Dias" +
            "6009SAO PAULO621405109kQSS8TQqj6304";

        var crc = PixBrCodeGenerator.CalcularCrc16(semCrc);

        Assert.Equal("198F", crc);
    }

    [Fact]
    public void GerarPayload_UsaGuiEmMaiusculo()
    {
        // GUI em maiúsculo é o que um Pix copia-e-cola real, gerado por banco, usa —
        // minúsculo passava em validadores genéricos mas não era reconhecido de verdade.
        var payload = PixBrCodeGenerator.GerarPayload("chave@exemplo.com", "Fulano", 10m, "x1");

        Assert.Contains("BR.GOV.BCB.PIX", payload);
        Assert.DoesNotContain("br.gov.bcb.pix", payload);
    }

    [Fact]
    public void GerarPayload_CrcNoFinalBateComORestoDoPayload()
    {
        var payload = PixBrCodeGenerator.GerarPayload("11999998888", "Fulano de Tal", 45.50m, "racha7");

        var semCrc = payload[..^4];
        var crcNoPayload = payload[^4..];

        Assert.Equal(PixBrCodeGenerator.CalcularCrc16(semCrc), crcNoPayload);
    }

    [Fact]
    public void GerarPayload_ContemCamposObrigatoriosDoEmv()
    {
        var payload = PixBrCodeGenerator.GerarPayload("11999998888", "Fulano de Tal", 45.50m, "racha7");

        Assert.StartsWith("000201", payload); // Payload Format Indicator
        Assert.Contains("BR.GOV.BCB.PIX", payload);
        Assert.Contains("11999998888", payload); // chave Pix
        Assert.Contains("5303986", payload); // moeda BRL
        Assert.Contains("540545.50", payload); // valor formatado com 2 casas
        Assert.Contains("5802BR", payload); // país
    }

    [Fact]
    public void GerarPayload_SemTxId_UsaAsteriscos()
    {
        var payload = PixBrCodeGenerator.GerarPayload("chave@exemplo.com", "Fulano", 10m, null);

        Assert.Contains("0503***", payload);
    }

    [Fact]
    public void GerarPayload_NomeComAcentoEEspacial_VemSanitizado()
    {
        var payload = PixBrCodeGenerator.GerarPayload("chave@exemplo.com", "João Ção", 10m, "x1");

        Assert.Contains("JOAO CAO", payload);
        Assert.DoesNotContain("João", payload);
    }

    [Fact]
    public void GerarPayload_NomeMuitoLongo_TruncaEm25Caracteres()
    {
        var nomeLongo = new string('A', 40);

        var payload = PixBrCodeGenerator.GerarPayload("chave@exemplo.com", nomeLongo, 10m, "x1");

        Assert.Contains("59" + "25" + new string('A', 25), payload);
    }
}
