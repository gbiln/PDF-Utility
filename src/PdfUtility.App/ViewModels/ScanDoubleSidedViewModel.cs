// src/PdfUtility.App/ViewModels/ScanDoubleSidedViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace PdfUtility.App.ViewModels;

public partial class ScanDoubleSidedViewModel : ObservableObject
{
    private readonly IScannerBackend _scanner;
    private readonly IPdfBuilder _pdfBuilder;
    private readonly IUserSettings _userSettings;
    private ScanSession _session = new();
    private string _sessionDirectory = string.Empty;

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
    private ScanSessionState _sessionState = ScanSessionState.Idle;

    [ObservableProperty] private string _statusMessage = "Ready to scan.";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _showErrorBanner;
    [ObservableProperty] private bool _showMismatchWarning;
    [ObservableProperty] private string _mismatchWarningText = string.Empty;

    public ObservableCollection<PageThumbnailViewModel> Thumbnails { get; } = new();

    // Settings (set by MainViewModel via binding or DI)
    public ScanOptions CurrentScanOptions { get; set; } = new();

    public ScanDoubleSidedViewModel(
        IScannerBackend scanner,
        IPdfBuilder pdfBuilder,
        IUserSettings userSettings)
    {
        _scanner = scanner;
        _pdfBuilder = pdfBuilder;
        _userSettings = userSettings;
    }

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
    private bool CanStartBatch1() => SessionState == ScanSessionState.Idle;

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
        SessionState = ScanSessionState.Batch1Complete;
        StatusMessage = $"Batch 1 complete ({_session.Batch1.Count} pages). Now scan the other side.";
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
        // Go directly to MergeReady — no need for intermediate Batch2Complete state
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

        var mergedPages = GetMergedPages();
        var options = new PdfBuildOptions
        {
            Format = prefs.PdfFormat,
            JpegQuality = prefs.JpegQuality,
            PaperSize = prefs.PaperSize
        };

        try
        {
            await _pdfBuilder.BuildAsync(mergedPages, options, dialog.FileName);
            SessionState = ScanSessionState.Saved;
            StatusMessage = $"Saved: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
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
            if (Thumbnails.Count > 0) Thumbnails.RemoveAt(Thumbnails.Count - 1);
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
                CurrentScanOptions, targetBatch, _sessionDirectory, startIndex))
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
                    dispatcher.Invoke(() => Thumbnails.Add(thumb));
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

    [RelayCommand]
    private async Task ReplacePage(PageThumbnailViewModel thumb)
    {
        // Flatbed replacement — implemented in Task 12
        await Task.CompletedTask;
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
}
