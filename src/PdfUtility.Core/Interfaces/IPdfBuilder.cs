// src/PdfUtility.Core/Interfaces/IPdfBuilder.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IPdfBuilder
{
    /// <summary>
    /// Assembles IPageSource images into a PDF at outputPath.
    /// Each image is JPEG-compressed at options.JpegQuality.
    /// </summary>
    Task BuildAsync(
        IEnumerable<IPageSource> pages,
        PdfBuildOptions options,
        string outputPath);
}
