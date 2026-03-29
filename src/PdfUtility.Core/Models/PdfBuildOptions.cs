namespace PdfUtility.Core.Models;

public class PdfBuildOptions
{
    public PdfFormat Format { get; init; } = PdfFormat.Standard;
    public int JpegQuality { get; init; } = 85;
    public PaperSize PaperSize { get; init; } = PaperSize.Letter;
}
