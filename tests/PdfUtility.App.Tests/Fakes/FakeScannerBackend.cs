// tests/PdfUtility.App.Tests/Fakes/FakeScannerBackend.cs
using System.Runtime.CompilerServices;
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

    /// <summary>Device names returned by GetDevicesAsync.</summary>
    public List<string> SimulatedDevices { get; } = new() { "Fake Scanner" };

    /// <summary>Name passed to the last SelectDevice call (null if cleared).</summary>
    public string? SelectedDeviceName { get; private set; }

    /// <summary>
    /// If set, GetDevicesAsync awaits this before returning — lets tests observe
    /// IsLoadingDevices == true before releasing enumeration.
    /// </summary>
    public TaskCompletionSource? GetDevicesGate { get; set; }

    /// <summary>If true, GetDevicesAsync throws InvalidOperationException.</summary>
    public bool GetDevicesShouldFail { get; set; }

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var paths = BatchQueue.Count > 0 ? BatchQueue.Dequeue() : new List<string>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ScannedPage(path, sourceBatch: batchNumber);
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
        int batchNumber,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (FlatbedShouldFail)
            throw new ScannerException("Fake flatbed failure");
        var path = NextFlatbedImagePath ?? "fake_flatbed.png";
        return Task.FromResult(new ScannedPage(path, sourceBatch: batchNumber));
    }

    public async Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (GetDevicesGate is { } gate)
            await gate.Task;
        if (GetDevicesShouldFail)
            throw new InvalidOperationException("Simulated device enumeration failure");
        return SimulatedDevices.AsReadOnly();
    }

    public void SelectDevice(string? deviceName) => SelectedDeviceName = deviceName;

    public Task InitialiseAsync()
    {
        InitialiseCalled = true;
        return Task.CompletedTask;
    }
}
