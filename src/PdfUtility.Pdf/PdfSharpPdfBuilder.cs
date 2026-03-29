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
        return Task.Run(() =>
        {
            using var document = new PdfDocument();
            document.Info.Title = "PDF Utility Document";

            foreach (var source in pages)
            {
                using var xImage = XImage.FromFile(source.ImagePath);

                // Portrait page dimensions in points.
                // For AutoDetect, derive from the image's own DPI metadata; otherwise use paper size.
                double drawW = options.PaperSize == PaperSize.AutoDetect
                    ? xImage.PointWidth
                    : PaperWidthPt(options.PaperSize);
                double drawH = options.PaperSize == PaperSize.AutoDetect
                    ? xImage.PointHeight
                    : PaperHeightPt(options.PaperSize);

                var page = document.AddPage();

                // For 90°/270° rotations the PDF page must be landscape to fit the rotated image
                bool isLandscape = source.Rotation == PageRotation.CW90 || source.Rotation == PageRotation.CW270;
                page.Width  = XUnit.FromPoint(isLandscape ? drawH : drawW);
                page.Height = XUnit.FromPoint(isLandscape ? drawW : drawH);

                using var gfx = XGraphics.FromPdfPage(page);
                switch (source.Rotation)
                {
                    case PageRotation.None:
                        gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                        break;
                    case PageRotation.CW90:
                        // Translate to top-right corner, then rotate CW 90°
                        gfx.TranslateTransform(page.Width.Point, 0);
                        gfx.RotateAtTransform(90, new XPoint(0, 0));
                        gfx.DrawImage(xImage, 0, 0, drawW, drawH);
                        break;
                    case PageRotation.CW180:
                        // Translate to bottom-right corner, then rotate 180°
                        gfx.TranslateTransform(page.Width.Point, page.Height.Point);
                        gfx.RotateAtTransform(180, new XPoint(0, 0));
                        gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                        break;
                    case PageRotation.CW270:
                        // Translate to bottom-left corner, then rotate CW 270° (= CCW 90°)
                        gfx.TranslateTransform(0, page.Height.Point);
                        gfx.RotateAtTransform(270, new XPoint(0, 0));
                        gfx.DrawImage(xImage, 0, 0, drawW, drawH);
                        break;
                }
            }

            // TODO: Apply options.JpegQuality — PdfSharp 6.2.4's PdfDocumentOptions does not
            // expose a JpegQuality property. When a future version adds it, set it here:
            // document.Options.JpegQuality = options.JpegQuality;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            document.Save(outputPath);
        });
    }

    private static double PaperWidthPt(PaperSize size) => size switch
    {
        PaperSize.Legal => 612,   // 8.5" × 72
        _               => 612,   // Letter: 8.5" × 72
    };

    private static double PaperHeightPt(PaperSize size) => size switch
    {
        PaperSize.Legal => 1008,  // 14" × 72
        _               => 792,   // Letter: 11" × 72
    };
}
