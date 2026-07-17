using QRCoder;

namespace RachaContas.Bot.Services;

/// <summary>Renderiza o payload do Pix copia-e-cola como PNG — puramente local, sem chamar nenhuma API.</summary>
public static class PixQrCodeImageService
{
    public static byte[] GerarPng(string payload)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }
}
