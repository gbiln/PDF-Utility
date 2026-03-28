namespace PdfUtility.Core.Models;

public enum ColorMode { Color, Grayscale, BlackAndWhite }
public enum PaperSize { Letter, Legal, AutoDetect }

public class ScanOptions
{
    public int Dpi { get; init; } = 300;
    public ColorMode ColorMode { get; init; } = ColorMode.Color;
    public PaperSize PaperSize { get; init; } = PaperSize.Letter;
}
