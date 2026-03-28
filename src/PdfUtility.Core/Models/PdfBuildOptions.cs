namespace PdfUtility.Core.Models;

public enum PdfFormat { Standard, PdfA }

public class PdfBuildOptions
{
    public PdfFormat Format { get; init; } = PdfFormat.Standard;
    public int JpegQuality { get; init; } = 85;
    public PaperSize PaperSize { get; init; } = PaperSize.Letter;
}
