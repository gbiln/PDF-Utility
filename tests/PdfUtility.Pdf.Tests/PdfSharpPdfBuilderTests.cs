// tests/PdfUtility.Pdf.Tests/PdfSharpPdfBuilderTests.cs
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using PdfUtility.Pdf;

namespace PdfUtility.Pdf.Tests;

public class PdfSharpPdfBuilderTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"PdfBuilderTests_{Guid.NewGuid():N}");

    public PdfSharpPdfBuilderTests() => Directory.CreateDirectory(_testDir);

    public void Dispose() => Directory.Delete(_testDir, recursive: true);

    private string CreateTestPng(string name, int widthPx = 100, int heightPx = 130)
    {
        // Create a minimal valid PNG using System.Drawing
        var path = Path.Combine(_testDir, name);
        using var bmp = new System.Drawing.Bitmap(widthPx, heightPx);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.White);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task BuildAsync_SinglePage_CreatesValidPdf()
    {
        var builder = new PdfSharpPdfBuilder();
        var pngPath = CreateTestPng("page1.png");
        var outputPath = Path.Combine(_testDir, "output.pdf");
        var pages = new[] { new TestPageSource(pngPath) };
        var opts = new PdfBuildOptions();

        await builder.BuildAsync(pages, opts, outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 0);
    }

    [Fact]
    public async Task BuildAsync_MultiplePages_CreatesFileWithCorrectSize()
    {
        var builder = new PdfSharpPdfBuilder();
        var pages = Enumerable.Range(1, 3)
            .Select(i => new TestPageSource(CreateTestPng($"page{i}.png")))
            .ToArray();
        var outputPath = Path.Combine(_testDir, "multi.pdf");

        await builder.BuildAsync(pages, new PdfBuildOptions(), outputPath);

        Assert.True(File.Exists(outputPath));
        // Multi-page PDF should be larger than single-page
        var singleOutputPath = Path.Combine(_testDir, "single.pdf");
        await builder.BuildAsync(new[] { pages[0] }, new PdfBuildOptions(), singleOutputPath);
        Assert.True(new FileInfo(outputPath).Length > new FileInfo(singleOutputPath).Length);
    }

    [Fact]
    public async Task BuildAsync_CW90Rotation_ProducesLandscapePage()
    {
        // A portrait PNG (100x130) rotated CW90 should produce a landscape page (130x100)
        var builder = new PdfSharpPdfBuilder();
        var pngPath = CreateTestPng("portrait.png", widthPx: 100, heightPx: 130);
        var outputPath = Path.Combine(_testDir, "rotated.pdf");
        var pages = new[] { new TestPageSource(pngPath) { Rotation = PageRotation.CW90 } };

        await builder.BuildAsync(pages, new PdfBuildOptions(), outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 0);
        // Verify page is landscape: load with PdfSharp and check dimensions
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(outputPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];
        Assert.True(page.Width > page.Height, $"Expected landscape (Width > Height) but got Width={page.Width}, Height={page.Height}");
    }

    private record TestPageSource(string ImagePath) : IPageSource
    {
        public PageRotation Rotation { get; set; } = PageRotation.None;
    }
}
