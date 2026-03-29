// tests/PdfUtility.App.Tests/Fakes/FakePdfImporter.cs
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Tests.Fakes;

public class FakePdfImporter : IPdfImporter
{
    /// <summary>Pages to return for each ImportAsync call, keyed by pdfPath (or null for any).</summary>
    public Queue<List<ImportedPage>> ImportQueue { get; } = new();

    /// <summary>If set, the next ImportAsync call throws this.</summary>
    public Exception? NextImportError { get; set; }

    public Task<IReadOnlyList<ImportedPage>> ImportAsync(
        string pdfPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (NextImportError is { } err)
        {
            NextImportError = null;
            return Task.FromException<IReadOnlyList<ImportedPage>>(err);
        }

        var pages = ImportQueue.Count > 0
            ? (IReadOnlyList<ImportedPage>)ImportQueue.Dequeue()
            : new List<ImportedPage>();

        return Task.FromResult(pages);
    }
}
