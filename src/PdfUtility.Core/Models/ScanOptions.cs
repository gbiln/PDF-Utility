namespace PdfUtility.Core.Models;

public class ScanOptions
{
    public int Dpi { get; init; } = 300;
    public ColorMode ColorMode { get; init; } = ColorMode.Color;
    public PaperSize PaperSize { get; init; } = PaperSize.Letter;
}
