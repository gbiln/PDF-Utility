// src/PdfUtility.Scanning/Naps2ScannerBackend.cs
using NAPS2.Images;
using NAPS2.Images.Wpf;
using NAPS2.Scan;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using ScanOptions = PdfUtility.Core.Models.ScanOptions;

namespace PdfUtility.Scanning;

public class Naps2ScannerBackend : IScannerBackend, IDisposable
{
    private ScanningContext? _context;
    private ScanController? _controller;
    private ScanDevice? _selectedDevice;
    private bool _disposed;

    public async Task InitialiseAsync()
    {
        _context = new ScanningContext(new WpfImageContext());
        _context.SetUpWin32Worker();
        _controller = new ScanController(_context);

        // Enumerate devices on background thread to pre-warm WIA
        var devices = await _controller.GetDeviceList(
            new NAPS2.Scan.ScanOptions { Driver = Driver.Wia });
        _selectedDevice = devices.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> GetAvailableDevicesAsync()
    {
        EnsureInitialised();
        var devices = await _controller!.GetDeviceList(
            new NAPS2.Scan.ScanOptions { Driver = Driver.Wia });
        return devices.Select(d => d.Name).ToList();
    }

    public async IAsyncEnumerable<ScannedPage> ScanBatchAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        Directory.CreateDirectory(sessionDirectory);

        var naps2Options = BuildNaps2Options(options, PaperSource.Feeder);

        // Collect pages via helper to allow try/catch around NAPS2 iteration
        // (C# forbids yield inside try/catch, so we buffer page paths first).
        var pages = await ScanBatchInternalAsync(naps2Options, batchNumber, sessionDirectory, startingPageIndex, cancellationToken);
        foreach (var page in pages)
            yield return page;
    }

    private async Task<List<ScannedPage>> ScanBatchInternalAsync(
        NAPS2.Scan.ScanOptions naps2Options,
        int batchNumber,
        string sessionDirectory,
        int startingPageIndex,
        CancellationToken cancellationToken)
    {
        var results = new List<ScannedPage>();
        int index = startingPageIndex;
        try
        {
            await foreach (var image in _controller!.Scan(naps2Options, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imagePath = Path.Combine(sessionDirectory, $"page_{index:D4}.png");
                try
                {
                    image.Save(imagePath, ImageFileFormat.Png, new ImageSaveOptions());
                }
                finally
                {
                    image.Dispose();
                }
                results.Add(new ScannedPage(imagePath, sourceBatch: batchNumber));
                index++;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ScannerException) { throw; }
        catch (Exception ex)
        {
            throw new ScannerException($"Scanner error: {ex.Message}", ex);
        }
        return results;
    }

    public async Task<ScannedPage> ScanSingleFlatbedAsync(
        ScanOptions options,
        int batchNumber,
        string sessionDirectory,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialised();
        Directory.CreateDirectory(sessionDirectory);

        var naps2Options = BuildNaps2Options(options, PaperSource.Flatbed);
        var imagePath = Path.Combine(sessionDirectory, $"replace_{pageIndex:D4}_{Guid.NewGuid():N}.png");

        try
        {
            await foreach (var image in _controller!.Scan(naps2Options, cancellationToken))
            {
                try
                {
                    image.Save(imagePath, ImageFileFormat.Png, new ImageSaveOptions());
                    return new ScannedPage(imagePath, sourceBatch: batchNumber);
                }
                finally
                {
                    image.Dispose();
                }
            }

            throw new ScannerException("Flatbed scan produced no image. Ensure a document is on the glass.");
        }
        catch (OperationCanceledException) { throw; }
        catch (ScannerException) { throw; }
        catch (Exception ex)
        {
            throw new ScannerException($"Scanner error: {ex.Message}", ex);
        }
    }

    private NAPS2.Scan.ScanOptions BuildNaps2Options(ScanOptions options, PaperSource source)
    {
        return new NAPS2.Scan.ScanOptions
        {
            Device = _selectedDevice,
            Driver = Driver.Wia,
            PaperSource = source,
            Dpi = options.Dpi,
            BitDepth = options.ColorMode switch
            {
                ColorMode.Color => BitDepth.Color,
                ColorMode.Grayscale => BitDepth.Grayscale,
                ColorMode.BlackAndWhite => BitDepth.BlackAndWhite,
                _ => BitDepth.Color
            },
            PageSize = options.PaperSize switch
            {
                PaperSize.Letter => PageSize.Letter,
                PaperSize.Legal => PageSize.Legal,
                PaperSize.AutoDetect => PageSize.Letter,
                _ => PageSize.Letter
            }
        };
    }

    private void EnsureInitialised()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Naps2ScannerBackend));
        if (_controller is null)
            throw new InvalidOperationException("Call InitialiseAsync before scanning.");
    }

    public void Dispose()
    {
        _disposed = true;
        _context?.Dispose();
    }
}
