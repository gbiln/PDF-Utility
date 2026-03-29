// src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PdfUtility.App.ViewModels;

public partial class ScanDoubleSidedViewModel : ObservableObject
{
    private readonly IScannerBackend _scanner;
    private readonly IPdfBuilder _pdfBuilder;
    private readonly IUserSettings _userSettings;
    private ScanSession _session = new();
    private string _sessionDirectory = string.Empty;

    // ── Scan settings (moved from MainViewModel toolbar) ─────────────────
    [ObservableProperty] private int _scanDpi = 300;
    [ObservableProperty] private ColorMode _colorMode = ColorMode.Color;
    [ObservableProperty] private PaperSize _paperSize = PaperSize.Letter;
    [ObservableProperty] private PdfFormat _pdfFormat = PdfFormat.Standard;
    [ObservableProperty] private ScanMode _scanMode = ScanMode.DoubleSided;

    public int[] DpiOptions { get; } = [150, 300, 600];
    public ColorMode[] ColorModeOptions { get; } = Enum.GetValues<ColorMode>();
    public PaperSize[] PaperSizeOptions { get; } = [PaperSize.Letter, PaperSize.Legal];
    public PdfFormat[] PdfFormatOptions { get; } = Enum.GetValues<PdfFormat>();
    public ScanMode[] ScanModeOptions { get; } = Enum.GetValues<ScanMode>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]
    [NotifyCanExecuteChangedFor(nameof(ContinueScanningCommand))]
    [NotifyCanExecuteChangedFor(nameof(DoneBatch1Command))]
    [NotifyCanExecuteChangedFor(nameof(ScanOtherSideCommand))]
    [NotifyCanExecuteChangedFor(nameof(DoneBatch2Command))]
    [NotifyCanExecuteChangedFor(nameof(MergeDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RescanLastPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(DoneCurrentBatchCommand))]
    [NotifyPropertyChangedFor(nameof(IsBatch1Complete))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(IsBatch1Actionable))]
    [NotifyPropertyChangedFor(nameof(IsBatch2Actionable))]
    [NotifyPropertyChangedFor(nameof(IsMergeReady))]
    private ScanSessionState _sessionState = ScanSessionState.Idle;

    public bool IsBatch1Complete => SessionState == ScanSessionState.Batch1Complete;

    // ── XAML visibility helpers ───────────────────────────────────────────
    // Settings section only shown when Idle (locked during active session)
    public bool IsIdle => SessionState == ScanSessionState.Idle;

    // Batch 1 action buttons (Continue + Done Scanning Front)
    public bool IsBatch1Actionable =>
        SessionState is ScanSessionState.Batch1Paused
                     or ScanSessionState.Batch1Error;

    // Batch 2 action buttons (Continue + Done Scanning Back)
    public bool IsBatch2Actionable =>
        SessionState is ScanSessionState.Batch2Paused
                     or ScanSessionState.Batch2Error;

    // Merge button
    public bool IsMergeReady => SessionState == ScanSessionState.MergeReady;

    [ObservableProperty] private string _statusMessage = "Ready to scan.";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _showErrorBanner;
    [ObservableProperty] private bool _showMismatchWarning;
    [ObservableProperty] private string _mismatchWarningText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatch1Command))]
    private string? _selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    private bool _isLoadingDevices;

    public ObservableCollection<string> AvailableDevices { get; } = new();

    public ObservableCollection<PageThumbnailViewModel> Thumbnails { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewOpen))]
    [NotifyPropertyChangedFor(nameof(IsNotPreviewOpen))]
    private PageThumbnailViewModel? _selectedThumbnail;

    public bool IsPreviewOpen => SelectedThumbnail != null;
    public bool IsNotPreviewOpen => SelectedThumbnail == null;

    public ScanDoubleSidedViewModel(
        IScannerBackend scanner,
        IPdfBuilder pdfBuilder,
        IUserSettings userSettings)
    {
        _scanner = scanner;
        _pdfBuilder = pdfBuilder;
        _userSettings = userSettings;
    }

    private ScanOptions BuildCurrentScanOptions() => new()
    {
        Dpi = ScanDpi,
        ColorMode = ColorMode,
        PaperSize = PaperSize
    };

    partial void OnSelectedDeviceChanged(string? value) => _scanner.SelectDevice(value);

    // ── Commands ──────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartBatch1))]
    private async Task StartBatch1()
    {
        _sessionDirectory = Path.Combine(
            Path.GetTempPath(), "PdfUtility", $"scan-{Guid.NewGuid():N}");
        _session = new ScanSession();
        Thumbnails.Clear();
        ErrorMessage = string.Empty;
        ShowErrorBanner = false;

        SessionState = ScanSessionState.Batch1Scanning;
        StatusMessage = "Scanning Batch 1 (front sides)…";
        await RunScanBatchAsync(targetBatch: 1, ScanSessionState.Batch1Paused, ScanSessionState.Batch1Error);
    }
    private bool CanStartBatch1() => SessionState == ScanSessionState.Idle && SelectedDevice != null;

    [RelayCommand(CanExecute = nameof(CanContinueScanning))]
    private async Task ContinueScanning()
    {
        ShowErrorBanner = false;
        ErrorMessage = string.Empty;

        bool isBatch1 = SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error;
        int targetBatch = isBatch1 ? 1 : 2;
        var scanningState = isBatch1 ? ScanSessionState.Batch1Scanning : ScanSessionState.Batch2Scanning;
        var pausedState = isBatch1 ? ScanSessionState.Batch1Paused : ScanSessionState.Batch2Paused;
        var errorState = isBatch1 ? ScanSessionState.Batch1Error : ScanSessionState.Batch2Error;

        SessionState = scanningState;
        StatusMessage = $"Scanning Batch {targetBatch} (continuing)…";
        await RunScanBatchAsync(targetBatch, pausedState, errorState);
    }
    private bool CanContinueScanning() =>
        SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error
                     or ScanSessionState.Batch2Paused or ScanSessionState.Batch2Error;

    [RelayCommand(CanExecute = nameof(CanDoneBatch1))]
    private Task DoneBatch1()
    {
        if (ScanMode == ScanMode.SingleSided)
        {
            BuildMergedThumbnails();
            SessionState = ScanSessionState.MergeReady;
            StatusMessage = $"Single-sided scan complete ({_session.Batch1.Count} pages). Ready to merge.";
        }
        else  // DoubleSided or AutoDetect — both require batch 2
        {
            SessionState = ScanSessionState.Batch1Complete;
            StatusMessage = $"Batch 1 complete ({_session.Batch1.Count} pages). Now scan the other side.";
        }
        return Task.CompletedTask;
    }
    // Enabled from both Batch1Paused (normal) and Batch1Error (error recovery)
    private bool CanDoneBatch1() =>
        SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error;

    [RelayCommand(CanExecute = nameof(CanScanOtherSide))]
    private async Task ScanOtherSide()
    {
        SessionState = ScanSessionState.Batch2Scanning;
        StatusMessage = "Scanning Batch 2 (back sides)…";
        await RunScanBatchAsync(targetBatch: 2, ScanSessionState.Batch2Paused, ScanSessionState.Batch2Error);
    }
    private bool CanScanOtherSide() => SessionState == ScanSessionState.Batch1Complete;

    [RelayCommand(CanExecute = nameof(CanDoneBatch2))]
    private Task DoneBatch2()
    {
        BuildMergedThumbnails();
        SessionState = ScanSessionState.MergeReady;
        StatusMessage = $"Batch 2 complete ({_session.Batch2.Count} pages). Ready to merge.";

        if (_session.HasPageCountMismatch)
        {
            ShowMismatchWarning = true;
            MismatchWarningText =
                $"⚠ Batch mismatch: {_session.Batch1.Count} vs {_session.Batch2.Count} pages — " +
                "extra pages will be appended at the end.";
        }
        return Task.CompletedTask;
    }
    // Enabled from both Batch2Paused (normal) and Batch2Error (error recovery)
    private bool CanDoneBatch2() =>
        SessionState is ScanSessionState.Batch2Paused or ScanSessionState.Batch2Error;

    [RelayCommand(CanExecute = nameof(CanMergeDocument))]
    private async Task MergeDocument()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Merged PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
        };

        var prefs = _userSettings.Load();
        if (!string.IsNullOrEmpty(prefs.DefaultSaveFolder))
            dialog.InitialDirectory = prefs.DefaultSaveFolder;

        if (dialog.ShowDialog() != true) return;

        StatusMessage = "Building PDF…";

        var mergedPages = Thumbnails
            .Where(t => t.ScannedPage != null)
            .Select(t => t.ScannedPage!)
            .ToList();
        var options = new PdfBuildOptions
        {
            Format = PdfFormat,
            JpegQuality = 85,
            PaperSize = PaperSize.AutoDetect  // scanner uses PaperSize for feed control; PDF derives dimensions from each image's own metadata
        };

        try
        {
            await _pdfBuilder.BuildAsync(mergedPages, options, dialog.FileName);
            SessionState = ScanSessionState.Saved;
            StatusMessage = $"Saved: {dialog.FileName}";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"Save failed (access denied): {ex.Message}";
        }
    }
    private bool CanMergeDocument() => SessionState == ScanSessionState.MergeReady;

    [RelayCommand(CanExecute = nameof(CanDiscardSession))]
    private Task DiscardSession()
    {
        _session.DiscardTempFiles();
        _session = new ScanSession();
        Thumbnails.Clear();
        ErrorMessage = string.Empty;
        ShowErrorBanner = false;
        ShowMismatchWarning = false;
        SessionState = ScanSessionState.Idle;
        StatusMessage = "Ready to scan.";
        return Task.CompletedTask;
    }
    private bool CanDiscardSession() =>
        SessionState != ScanSessionState.Idle && SessionState != ScanSessionState.Saved;

    [RelayCommand(CanExecute = nameof(CanRescanLastPage))]
    private async Task RescanLastPage()
    {
        bool isBatch1 = SessionState == ScanSessionState.Batch1Error;
        var batch = isBatch1 ? _session.Batch1 : _session.Batch2;
        int targetBatch = isBatch1 ? 1 : 2;

        // Remove last (potentially damaged) page
        if (batch.Count > 0)
        {
            var last = batch[^1];
            if (File.Exists(last.ImagePath)) File.Delete(last.ImagePath);
            batch.RemoveAt(batch.Count - 1);
            var thumb = Thumbnails.FirstOrDefault(t => t.ScannedPage == last);
            if (thumb != null) Thumbnails.Remove(thumb);
        }

        ShowErrorBanner = false;
        ErrorMessage = string.Empty;
        var scanningState = isBatch1 ? ScanSessionState.Batch1Scanning : ScanSessionState.Batch2Scanning;
        var pausedState = isBatch1 ? ScanSessionState.Batch1Paused : ScanSessionState.Batch2Paused;
        var errorState = isBatch1 ? ScanSessionState.Batch1Error : ScanSessionState.Batch2Error;

        SessionState = scanningState;
        await RunScanBatchAsync(targetBatch, pausedState, errorState);
    }
    private bool CanRescanLastPage()
    {
        if (SessionState == ScanSessionState.Batch1Error)
            return _session.Batch1.Count > 0;
        if (SessionState == ScanSessionState.Batch2Error)
            return _session.Batch2.Count > 0;
        return false;
    }

    // ── Core scan loop ────────────────────────────────────────────────

    private async Task RunScanBatchAsync(
        int targetBatch,
        ScanSessionState pausedState,
        ScanSessionState errorState)
    {
        var batch = targetBatch == 1 ? _session.Batch1 : _session.Batch2;
        int startIndex = batch.Count; // preserve count across Continue calls

        try
        {
            await foreach (var page in _scanner.ScanBatchAsync(
                BuildCurrentScanOptions(), targetBatch, _sessionDirectory, startIndex))
            {
                // page.SourceBatch is already set correctly by the scanner
                batch.Add(page);

                var thumb = new PageThumbnailViewModel
                {
                    ImagePath = page.ImagePath,
                    PageNumber = batch.Count,
                    SourceLabel = targetBatch == 1 ? "Front" : "Back",
                    ScannedPage = page
                };

                // Dispatch to UI thread if available; fall back to direct add (e.g. in tests)
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                    await dispatcher.InvokeAsync(() => Thumbnails.Add(thumb));
                else
                    Thumbnails.Add(thumb);

                StatusMessage = $"Scanning page {batch.Count}… (ADF feeding)";
            }

            // ADF tray empty — natural stop
            SessionState = pausedState;
            StatusMessage = $"Batch {targetBatch}: {batch.Count} page(s) scanned. Continue or declare done.";
        }
        catch (ScannerException ex)
        {
            SessionState = errorState;
            ErrorMessage = $"Paper jam or feeder error — fix the jam, then choose how to continue.\n({ex.Message})";
            ShowErrorBanner = true;
            StatusMessage = "Scanner error.";

            // Flag the last page as potentially partial
            if (batch.Count > 0)
                batch[^1].HasWarning = true;
            if (Thumbnails.Count > 0)
                Thumbnails[^1].HasWarning = true;
        }
    }

    // Called by the View after MergeDocumentCommand to actually save the PDF
    public List<ScannedPage> GetMergedPages() => _session.BuildMergedPages();

    private void BuildMergedThumbnails()
    {
        var merged = _session.BuildMergedPages();
        Thumbnails.Clear();
        for (int i = 0; i < merged.Count; i++)
        {
            Thumbnails.Add(new PageThumbnailViewModel
            {
                ImagePath = merged[i].ImagePath,
                PageNumber = i + 1,
                SourceLabel = merged[i].SourceBatch == 1 ? "Front" : "Back",
                ScannedPage = merged[i]
            });
        }
    }

    [RelayCommand]
    private async Task ReplacePage(PageThumbnailViewModel thumb)
    {
        if (thumb.ScannedPage is null) return;

        try
        {
            StatusMessage = "Scanning replacement page from flatbed…";
            var replacement = await _scanner.ScanSingleFlatbedAsync(
                BuildCurrentScanOptions(),
                thumb.ScannedPage.SourceBatch,
                _sessionDirectory,
                thumb.PageNumber - 1);

            thumb.ScannedPage.ReplaceImage(replacement.ImagePath);
            thumb.ImagePath = replacement.ImagePath;
            thumb.HasWarning = false;
            StatusMessage = $"Page {thumb.PageNumber} replaced.";
        }
        catch (ScannerException ex)
        {
            StatusMessage = $"Could not scan replacement page: {ex.Message}";
        }
    }

    [RelayCommand]
    private void MovePageToBeginning(PageThumbnailViewModel thumb)
    {
        int idx = Thumbnails.IndexOf(thumb);
        if (idx <= 0) return;
        Thumbnails.Move(idx, 0);
        RenumberThumbnails();
    }

    [RelayCommand]
    private void MovePageToEnd(PageThumbnailViewModel thumb)
    {
        int idx = Thumbnails.IndexOf(thumb);
        if (idx < 0 || idx == Thumbnails.Count - 1) return;
        Thumbnails.Move(idx, Thumbnails.Count - 1);
        RenumberThumbnails();
    }

    [RelayCommand]
    private void RemoveScanPage(PageThumbnailViewModel thumb)
    {
        int idx = Thumbnails.IndexOf(thumb);
        if (idx < 0) return;
        Thumbnails.RemoveAt(idx);
        RenumberThumbnails();
    }

    private void RenumberThumbnails()
    {
        for (int i = 0; i < Thumbnails.Count; i++)
            Thumbnails[i].PageNumber = i + 1;
    }

    [RelayCommand(CanExecute = nameof(CanDoneCurrentBatch))]
    private Task DoneCurrentBatch()
    {
        if (SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error)
            return DoneBatch1();
        return DoneBatch2();
    }
    private bool CanDoneCurrentBatch() =>
        SessionState is ScanSessionState.Batch1Paused or ScanSessionState.Batch1Error
                     or ScanSessionState.Batch2Paused or ScanSessionState.Batch2Error;

    [RelayCommand]
    private void PreviewPage(PageThumbnailViewModel thumb) => SelectedThumbnail = thumb;

    [RelayCommand]
    private void ClosePreview() => SelectedThumbnail = null;

    [RelayCommand(CanExecute = nameof(CanRefreshDevices))]
    private async Task RefreshDevices()
    {
        IsLoadingDevices = true;
        StatusMessage = "Scanning for devices…";
        try
        {
            var devices = await _scanner.GetDevicesAsync();
            AvailableDevices.Clear();            // ← only clear once we have fresh data
            foreach (var d in devices)
                AvailableDevices.Add(d);

            if (SelectedDevice != null && !devices.Contains(SelectedDevice))
                SelectedDevice = null;

            if (devices.Count == 1 && SelectedDevice == null)  // ← guard: avoid spurious double-call
                SelectedDevice = devices[0];

            StatusMessage = devices.Count == 0
                ? "No scanners found — check network connection and click ↻ to retry."
                : "Ready to scan.";
        }
        catch (OperationCanceledException) { throw; }  // ← rethrow cancellation, don't swallow
        catch (Exception)
        {
            AvailableDevices.Clear();
            SelectedDevice = null;
            StatusMessage = "Could not enumerate scanners — check network connection and click ↻ to retry.";
        }
        finally
        {
            IsLoadingDevices = false;
        }
    }
    private bool CanRefreshDevices() => !IsLoadingDevices;
}
