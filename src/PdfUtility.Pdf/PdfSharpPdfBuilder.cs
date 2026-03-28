// src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.Pdf;

public class PdfSharpPdfBuilder : IPdfBuilder
{
    // A4/Letter dimensions at 300 DPI — we size pages to match the images.
    public Task BuildAsync(
        IEnumerable<IPageSource> pages,
        PdfBuildOptions options,
        string outputPath)
    {
        using var document = new PdfDocument();
        document.Info.Title = "PDF Utility Document";

        foreach (var source in pages)
        {
            using var xImage = XImage.FromFile(source.ImagePath);

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(xImage.PointWidth);
            page.Height = XUnit.FromPoint(xImage.PointHeight);

            // Apply rotation if needed
            using var gfx = XGraphics.FromPdfPage(page);
            if (source.Rotation == PageRotation.None)
            {
                gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            }
            else
            {
                var cx = page.Width.Point / 2;
                var cy = page.Height.Point / 2;
                gfx.RotateAtTransform((double)source.Rotation, new XPoint(cx, cy));
                gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        document.Save(outputPath);
        return Task.CompletedTask;
    }
}
