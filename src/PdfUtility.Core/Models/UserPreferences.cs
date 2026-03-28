namespace PdfUtility.Core.Models;

public class UserPreferences
{
    public int ScanDpi { get; set; } = 300;
    public ColorMode ColorMode { get; set; } = ColorMode.Color;
    public PdfFormat PdfFormat { get; set; } = PdfFormat.Standard;
    public PaperSize PaperSize { get; set; } = PaperSize.Letter;
    public int JpegQuality { get; set; } = 85;
    public string DefaultSaveFolder { get; set; } = string.Empty;
    public string ScannerBackend { get; set; } = "Naps2";
}
