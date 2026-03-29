namespace PdfUtility.Core.Models;

public class ImportedPage : IPageSource
{
    public string ImagePath { get; }
    public int SourcePageIndex { get; }
    public string SourceFileName { get; }
    public PageRotation Rotation { get; set; } = PageRotation.None;

    public ImportedPage(string imagePath, int sourcePageIndex, string sourceFileName)
    {
        ImagePath = imagePath;
        SourcePageIndex = sourcePageIndex;
        SourceFileName = sourceFileName;
    }
}
