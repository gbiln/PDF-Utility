// src/PdfUtility.Core/Models/ScannedPage.cs
namespace PdfUtility.Core.Models;

public class ScannedPage : IPageSource
{
    public string ImagePath { get; private set; }
    public PageRotation Rotation { get; set; } = PageRotation.None;
    public int SourceBatch { get; }
    public bool HasWarning { get; set; }

    public ScannedPage(string imagePath, int sourceBatch)
    {
        ImagePath = imagePath;
        SourceBatch = sourceBatch;
    }

    public void ReplaceImage(string newPath)
    {
        File.Delete(ImagePath);   // delete previous temp PNG immediately
        ImagePath = newPath;
        HasWarning = false;
    }
}
