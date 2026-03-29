using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IPdfImporter
{
    Task<IReadOnlyList<ImportedPage>> ImportAsync(
        string pdfPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
