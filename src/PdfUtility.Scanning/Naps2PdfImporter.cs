// src/PdfUtility.Scanning/Naps2PdfImporter.cs
using NAPS2.Images;
using NAPS2.Images.Wpf;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.Scanning;

public class Naps2PdfImporter : IPdfImporter, IDisposable
{
    private readonly ScanningContext _context = new(new WpfImageContext());
    private bool _disposed;

    public async Task<IReadOnlyList<ImportedPage>> ImportAsync(
        string pdfPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sourceFileName = Path.GetFileNameWithoutExtension(pdfPath);
        var safeFileName = string.Concat(sourceFileName.Split(Path.GetInvalidFileNameChars()));
        var pages = new List<ImportedPage>();
        int index = 0;

        try
        {
            var importer = new PdfImporter(_context, null);
            var importParams = new ImportParams();

            await foreach (var image in importer.Import(pdfPath, importParams, (NAPS2.Util.ProgressHandler)cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = $"{safeFileName}_{Guid.NewGuid():N8}_page_{index:D4}.png";
                var imagePath = Path.Combine(outputDirectory, fileName);
                try
                {
                    image.Save(imagePath, ImageFileFormat.Png, new ImageSaveOptions());
                }
                finally
                {
                    image.Dispose();
                }
                pages.Add(new ImportedPage(imagePath, index, Path.GetFileName(pdfPath)));
                index++;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new PdfImportException($"Failed to import '{Path.GetFileName(pdfPath)}': {ex.Message}", ex);
        }

        if (pages.Count == 0 && !cancellationToken.IsCancellationRequested)
            throw new PdfImportException($"No pages found in '{Path.GetFileName(pdfPath)}'.");

        return pages;
    }

    public void Dispose()
    {
        _disposed = true;
        _context.Dispose();
    }
}
