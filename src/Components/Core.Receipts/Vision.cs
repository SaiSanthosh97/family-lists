namespace FamilyLists.Core.Receipts;

/// <summary>Reads printed text from an image (Android: Google ML Kit, on the phone).</summary>
public interface ITextRecognizer
{
    Task<string> ReadTextAsync(byte[] image, CancellationToken cancellationToken = default);

    /// <summary>The text plus each word's box, so a price can be put on the same row as its product. Recognisers
    /// that can't give positions return the text only.</summary>
    async Task<RecognizedText> ReadWordsAsync(byte[] image, CancellationToken cancellationToken = default) =>
        new(await ReadTextAsync(image, cancellationToken), []);
}

/// <summary>OCR output: the text (line by line) and, when available, each word with its box in image pixels.</summary>
public sealed record RecognizedText(string Text, IReadOnlyList<LayoutWord> Words);

/// <summary>One word and where it sits on the page (pixels, origin top-left).</summary>
public sealed record LayoutWord(string Text, double Left, double Top, double Right, double Bottom, int Page = 0)
{
    public double CenterY => (Top + Bottom) / 2;

    public double Height => Math.Max(Bottom - Top, 0.01);
}

public enum BarcodeFormat
{
    Unknown = 0,
    Pdf417,
    Aztec,
    QrCode,
    DataMatrix,
    Code128,
    Other,
}

public sealed record Barcode(BarcodeFormat Format, string Value);

/// <summary>Finds barcodes in an image (Android: Google ML Kit). Used to scan a group invite QR code.</summary>
public interface IBarcodeScanner
{
    Task<IReadOnlyList<Barcode>> ScanAsync(byte[] image, CancellationToken cancellationToken = default);
}

/// <summary>Renders PDF pages to images (Android: PdfRenderer, sandboxed by the OS), for bills shared as PDF.</summary>
public interface IPdfRasterizer
{
    Task<IReadOnlyList<byte[]>> RenderPagesAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken = default);
}
