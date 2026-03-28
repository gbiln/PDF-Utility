// tests/PdfUtility.Core.Tests/Models/ScannedPageTests.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Tests.Models;

public class ScannedPageTests
{
    [Fact]
    public void Constructor_SetsImagePathAndBatch()
    {
        var page = new ScannedPage("path/to/page.png", sourceBatch: 1);
        Assert.Equal("path/to/page.png", page.ImagePath);
        Assert.Equal(1, page.SourceBatch);
        Assert.Equal(PageRotation.None, page.Rotation);
        Assert.False(page.HasWarning);
    }

    [Fact]
    public void ReplaceImage_UpdatesPathAndClearsWarning()
    {
        // Create a real temp file so File.Delete doesn't throw
        var oldPath = Path.GetTempFileName();
        var newPath = Path.GetTempFileName();
        try
        {
            var page = new ScannedPage(oldPath, sourceBatch: 1) { HasWarning = true };
            page.ReplaceImage(newPath);
            Assert.Equal(newPath, page.ImagePath);
            Assert.False(page.HasWarning);
            Assert.False(File.Exists(oldPath)); // old file was deleted
        }
        finally
        {
            if (File.Exists(newPath)) File.Delete(newPath);
        }
    }
}
