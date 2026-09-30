using QRCoder;
using Fuse.Application.Common.Payments;

namespace Fuse.Infrastructure.Payments;

internal sealed class QrCodes : IQrCodes
{
    /// <summary>PNG (no System.Drawing, so it works on any host; legacy's bitmap version failed on some machines).</summary>
    public string PngDataUrl(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(20);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }
}
