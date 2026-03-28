// src/PdfUtility.Core/Interfaces/IScannerBackend.cs
using System.Collections.Generic;
using System.Threading;
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IScannerBackend
{
    /// <summary>
    /// Streams pages from the ADF one at a time as they are scanned.
    /// The IAsyncEnumerable completes when the ADF tray is empty.
    /// Throws ScannerException on feeder error or jam.
    /// </summary>
    IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a single page from the flatbed glass.
    /// Throws ScannerException on failure.
    /// </summary>
    Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all available scanner device names.</summary>
    Task<IReadOnlyList<string>> GetAvailableDevicesAsync();

    /// <summary>Pre-warms the scanner context (call at app startup).</summary>
    Task InitialiseAsync();
}
