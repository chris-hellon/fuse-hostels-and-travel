namespace Fuse.Contracts.Identity;

/// <summary>A staff member's booking QR code for a tour: guests who book through it are credited to them.</summary>
public sealed record TourQrCode(string TourName, string Url, string PngDataUrl);

/// <summary>
/// The signed-in staff member's QR codes (legacy's Account › QR Codes): one per tour on the website or set to show its
/// booking QR code. None for suppliers.
/// </summary>
[Get("me/tour-qr-codes"), Staff]
public sealed record ListMyTourQrCodes : IQuery<IReadOnlyList<TourQrCode>>;
