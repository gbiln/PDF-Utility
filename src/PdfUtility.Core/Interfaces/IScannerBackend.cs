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
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a single page from the flatbed glass.
    /// Throws ScannerException on failure.
    /// </summary>
    Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all available scanner device names (re-enumerates each call).
    /// Replaces GetAvailableDevicesAsync.
    /// </summary>
    Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active device by name. Pass null to clear the selection.
    /// Callers must only pass names sourced from the most recent GetDevicesAsync result.
    /// </summary>
    void SelectDevice(string? deviceName);

    /// <summary>Pre-warms the scanner context (call at app startup).</summary>
    Task InitialiseAsync();
}
