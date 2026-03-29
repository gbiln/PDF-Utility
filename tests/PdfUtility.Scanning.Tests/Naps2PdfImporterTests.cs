// tests/PdfUtility.Scanning.Tests/Naps2PdfImporterTests.cs
using PdfUtility.Core.Exceptions;
using PdfUtility.Scanning;

namespace PdfUtility.Scanning.Tests;

public class Naps2PdfImporterTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"Naps2ImporterTests_{Guid.NewGuid():N}");

    public Naps2PdfImporterTests() => Directory.CreateDirectory(_testDir);

    public void Dispose() => Directory.Delete(_testDir, recursive: true);

    /// <summary>Creates a minimal 2-page PDF using PdfSharp for test purposes.</summary>
    private string CreateTwoPagePdf(string name)
    {
        var path = Path.Combine(_testDir, name);
        using var doc = new PdfSharp.Pdf.PdfDocument();
        for (int i = 0; i < 2; i++)
        {
            var page = doc.AddPage();
            var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            // Draw a rectangle — no font resolver required
            gfx.DrawRectangle(PdfSharp.Drawing.XBrushes.LightBlue,
                new PdfSharp.Drawing.XRect(50, 50, 200, 100));
        }
        doc.Save(path);
        return path;
    }

    private string CreateCorruptPdf(string name)
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllText(path, "this is not a pdf");
        return path;
    }

    [Fact]
    public async Task ImportAsync_TwoPagePdf_ReturnsCorrectPageCount()
    {
        var pdfPath = CreateTwoPagePdf("two-page.pdf");
        using var importer = new Naps2PdfImporter();
        var outDir = Path.Combine(_testDir, "out");
        Directory.CreateDirectory(outDir);

        var pages = await importer.ImportAsync(pdfPath, outDir);

        Assert.Equal(2, pages.Count);
    }

    [Fact]
    public async Task ImportAsync_TwoPagePdf_EachPageIsNonEmptyPng()
    {
        var pdfPath = CreateTwoPagePdf("two-page.pdf");
        using var importer = new Naps2PdfImporter();
        var outDir = Path.Combine(_testDir, "out");
        Directory.CreateDirectory(outDir);

        var pages = await importer.ImportAsync(pdfPath, outDir);

        foreach (var page in pages)
        {
            Assert.True(File.Exists(page.ImagePath), $"Image file missing: {page.ImagePath}");
            Assert.True(new FileInfo(page.ImagePath).Length > 0, $"Image file is empty: {page.ImagePath}");
            // Verify it's a readable image
            using var bmp = System.Drawing.Image.FromFile(page.ImagePath);
            Assert.NotNull(bmp);
        }
    }

    [Fact]
    public async Task ImportAsync_CorruptPdf_ThrowsPdfImportException()
    {
        var corruptPath = CreateCorruptPdf("corrupt.pdf");
        using var importer = new Naps2PdfImporter();
        var outDir = Path.Combine(_testDir, "out");
        Directory.CreateDirectory(outDir);

        await Assert.ThrowsAsync<PdfImportException>(() =>
            importer.ImportAsync(corruptPath, outDir));
    }
}
