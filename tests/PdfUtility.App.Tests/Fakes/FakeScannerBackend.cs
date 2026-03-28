// tests/PdfUtility.App.Tests/Fakes/FakeScannerBackend.cs
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Tests.Fakes;

public class FakeScannerBackend : IScannerBackend
{
    /// <summary>Pages to yield from the next ScanBatchAsync call.</summary>
    public Queue<List<string>> BatchQueue { get; } = new();

    /// <summary>If set, ScanBatchAsync throws this after yielding any queued pages.</summary>
    public Exception? NextScanError { get; set; }

    /// <summary>Path returned by the next ScanSingleFlatbedAsync call.</summary>
    public string? NextFlatbedImagePath { get; set; }

    /// <summary>If true, ScanSingleFlatbedAsync throws ScannerException.</summary>
    public bool FlatbedShouldFail { get; set; }

    public bool InitialiseCalled { get; private set; }

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken = default)
    {
        var paths = BatchQueue.Count > 0 ? BatchQueue.Dequeue() : new List<string>();
        int index = startingPageIndex;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ScannedPage(path, sourceBatch: 0);
            index++;
        }
        if (NextScanError is not null)
        {
            var ex = NextScanError;
            NextScanError = null;
            throw ex;
        }
    }

    public Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (FlatbedShouldFail)
            throw new ScannerException("Fake flatbed failure");
        var path = NextFlatbedImagePath ?? "fake_flatbed.png";
        return Task.FromResult(new ScannedPage(path, sourceBatch: 0));
    }

    public Task<IReadOnlyList<string>> GetAvailableDevicesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(["Fake Scanner"]);

    public Task InitialiseAsync()
    {
        InitialiseCalled = true;
        return Task.CompletedTask;
    }
}
