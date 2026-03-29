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

                var imageWidth = xImage.PointWidth;
                var imageHeight = xImage.PointHeight;

                var page = document.AddPage();

                // For 90°/270° rotations the PDF page must be landscape to fit the rotated image
                if (source.Rotation == PageRotation.CW90 || source.Rotation == PageRotation.CW270)
                {
                    page.Width = XUnit.FromPoint(imageHeight);
                    page.Height = XUnit.FromPoint(imageWidth);
                }
                else
                {
                    page.Width = XUnit.FromPoint(imageWidth);
                    page.Height = XUnit.FromPoint(imageHeight);
                }

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
                        gfx.DrawImage(xImage, 0, 0, imageWidth, imageHeight);
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
                        gfx.DrawImage(xImage, 0, 0, imageWidth, imageHeight);
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
}
