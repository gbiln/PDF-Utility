// src/PdfUtility.Pdf/PdfSharpPdfBuilder.cs
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using System.IO;
using System.Windows.Media.Imaging;

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
                string? tempJpeg = null;
                try
                {
                    tempJpeg = Path.Combine(
                        Path.GetTempPath(),
                        $"pdfutility_jpeg_{Guid.NewGuid():N}.jpg");
                    ConvertPngToJpeg(source.ImagePath, tempJpeg, options.JpegQuality);

                    using var xImage = XImage.FromFile(tempJpeg);

                    double drawW = options.PaperSize == PaperSize.AutoDetect
                        ? xImage.PointWidth
                        : PaperWidthPt(options.PaperSize);
                    double drawH = options.PaperSize == PaperSize.AutoDetect
                        ? xImage.PointHeight
                        : PaperHeightPt(options.PaperSize);

                    var page = document.AddPage();

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
                            gfx.TranslateTransform(page.Width.Point, 0);
                            gfx.RotateAtTransform(90, new XPoint(0, 0));
                            gfx.DrawImage(xImage, 0, 0, drawW, drawH);
                            break;
                        case PageRotation.CW180:
                            gfx.TranslateTransform(page.Width.Point, page.Height.Point);
                            gfx.RotateAtTransform(180, new XPoint(0, 0));
                            gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                            break;
                        case PageRotation.CW270:
                            gfx.TranslateTransform(0, page.Height.Point);
                            gfx.RotateAtTransform(270, new XPoint(0, 0));
                            gfx.DrawImage(xImage, 0, 0, drawW, drawH);
                            break;
                    }
                }
                finally
                {
                    if (tempJpeg != null)
                        try { File.Delete(tempJpeg); } catch { }
                }
            }

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

    private static void ConvertPngToJpeg(string pngPath, string jpegPath, int quality)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(Path.GetFullPath(pngPath));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze(); // releases the file handle

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(jpegPath);
        encoder.Save(fs);
    }
}
