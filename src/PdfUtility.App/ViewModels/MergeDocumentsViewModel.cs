// src/PdfUtility.App/ViewModels/MergeDocumentsViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PdfUtility.App.ViewModels;

public enum MergeSessionState { Idle, FilesQueued, LoadingPages, PagesLoaded, Merging }

public partial class MergeDocumentsViewModel : ObservableObject
{
    private readonly IPdfImporter _importer;
    private readonly IPdfBuilder _pdfBuilder;
    private readonly IUserSettings _userSettings;

    // Parallel list to Pages — tracks the ImportedPage that backs each thumbnail
    private readonly List<ImportedPage> _pageData = [];

    // Temp directory for current session's PNG renderings
    private string _sessionTempDir = string.Empty;

    // CancellationTokenSource for LoadPages — cancelled by DiscardSession
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFileUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveFileDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSessionCommand))]
    [NotifyPropertyChangedFor(nameof(IsShowingFileList))]
    private MergeSessionState _sessionState = MergeSessionState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    // Computed display helpers for status bar binding
    public string DisplayStatus => string.IsNullOrEmpty(ErrorMessage) ? StatusMessage : ErrorMessage;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewOpen))]
    [NotifyPropertyChangedFor(nameof(IsNotPreviewOpen))]
    private PageThumbnailViewModel? _previewPage;

    public bool IsPreviewOpen => PreviewPage != null;
    public bool IsNotPreviewOpen => PreviewPage == null;

    // True when the file list should be shown in the main content area (before loading)
    public bool IsShowingFileList =>
        SessionState is MergeSessionState.Idle or MergeSessionState.FilesQueued;

    [ObservableProperty] private bool _isLoadingPages;

    public ObservableCollection<MergeFileViewModel> FileQueue { get; } = new();
    public ObservableCollection<PageThumbnailViewModel> Pages { get; } = new();

    public MergeDocumentsViewModel(
        IPdfImporter importer,
        IPdfBuilder pdfBuilder,
        IUserSettings userSettings)
    {
        _importer = importer;
        _pdfBuilder = pdfBuilder;
        _userSettings = userSettings;
    }

    // ── File Queue Commands ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanModifyQueue))]
    private void AddFiles(IEnumerable<string> paths)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        foreach (var path in paths)
        {
            FileQueue.Add(new MergeFileViewModel
            {
                FilePath = path,
                FileName = Path.GetFileName(path)
            });
        }
        if (FileQueue.Count > 0 && SessionState == MergeSessionState.Idle)
            SessionState = MergeSessionState.FilesQueued;
    }
    private bool CanModifyQueue() =>
        SessionState is MergeSessionState.Idle
            or MergeSessionState.FilesQueued
            or MergeSessionState.PagesLoaded;

    [RelayCommand(CanExecute = nameof(CanModifyQueue))]
    private void RemoveFile(MergeFileViewModel file)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        FileQueue.Remove(file);
        if (FileQueue.Count == 0)
        {
            SessionState = MergeSessionState.Idle;
            Pages.Clear();
            _pageData.Clear();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyQueue))]
    private void MoveFileUp(MergeFileViewModel file)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        int idx = FileQueue.IndexOf(file);
        if (idx > 0) FileQueue.Move(idx, idx - 1);
    }

    [RelayCommand(CanExecute = nameof(CanModifyQueue))]
    private void MoveFileDown(MergeFileViewModel file)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        int idx = FileQueue.IndexOf(file);
        if (idx >= 0 && idx < FileQueue.Count - 1) FileQueue.Move(idx, idx + 1);
    }

    // ── Load Pages Command ────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLoadPages))]
    private async Task LoadPages()
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;

        DeleteTempDir();  // clean up any prior session temp dir
        _sessionTempDir = Path.Combine(
            Path.GetTempPath(), $"PdfUtility_Merge_{Guid.NewGuid():N}");

        try { Directory.CreateDirectory(_sessionTempDir); }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create temp directory: {ex.Message}";
            return;
        }

        Pages.Clear();
        _pageData.Clear();
        SessionState = MergeSessionState.LoadingPages;
        IsLoadingPages = true;

        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        var failures = new List<string>();
        int pageNumber = 1;

        foreach (var file in FileQueue.ToList())
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var importedPages = await _importer.ImportAsync(file.FilePath, _sessionTempDir, ct);
                foreach (var ip in importedPages)
                {
                    _pageData.Add(ip);
                    var thumb = new PageThumbnailViewModel
                    {
                        ImagePath = ip.ImagePath,
                        PageNumber = pageNumber++,
                        SourceLabel = file.FileName
                    };
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null)
                        await dispatcher.InvokeAsync(() => Pages.Add(thumb));
                    else
                        Pages.Add(thumb);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (PdfImportException ex) { failures.Add($"{file.FileName}: {ex.Message}"); }
            catch (Exception ex) { failures.Add($"{file.FileName}: {ex.Message}"); }
        }

        IsLoadingPages = false;

        if (ct.IsCancellationRequested)
        {
            // Cancelled by DiscardSession — clean up partial state
            Pages.Clear();
            _pageData.Clear();
            DeleteTempDir();
            SessionState = MergeSessionState.FilesQueued;
            return;
        }

        if (_pageData.Count == 0)
        {
            DeleteTempDir();
            SessionState = MergeSessionState.FilesQueued;
            ErrorMessage = failures.Count > 0
                ? $"No pages could be loaded. Failed: {string.Join(", ", failures)}"
                : "No pages could be loaded. Check that the selected files are valid, unencrypted PDFs.";
            return;
        }

        SessionState = MergeSessionState.PagesLoaded;

        if (failures.Count > 0)
            ErrorMessage = $"Some files could not be imported: {string.Join(", ", failures)}. Loaded {_pageData.Count} pages from remaining files.";
        else
            StatusMessage = $"Loaded {_pageData.Count} page(s) from {FileQueue.Count} file(s).";
    }
    private bool CanLoadPages() =>
        SessionState == MergeSessionState.FilesQueued && FileQueue.Count > 0;

    // ── Page Order Commands ───────────────────────────────────────────

    [RelayCommand]
    private void MovePageToBeginning(PageThumbnailViewModel thumb)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        int idx = Pages.IndexOf(thumb);
        if (idx <= 0) return;
        Pages.Move(idx, 0);
        _pageData.Insert(0, _pageData[idx]);
        _pageData.RemoveAt(idx + 1);
        RenumberPages();
    }

    [RelayCommand]
    private void MovePageToEnd(PageThumbnailViewModel thumb)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        int idx = Pages.IndexOf(thumb);
        if (idx < 0 || idx == Pages.Count - 1) return;
        Pages.Move(idx, Pages.Count - 1);
        var ip = _pageData[idx];
        _pageData.RemoveAt(idx);
        _pageData.Add(ip);
        RenumberPages();
    }

    [RelayCommand]
    private void RemovePage(PageThumbnailViewModel thumb)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        int idx = Pages.IndexOf(thumb);
        if (idx < 0) return;
        Pages.RemoveAt(idx);
        _pageData.RemoveAt(idx);
        RenumberPages();
        if (Pages.Count == 0)
            SessionState = MergeSessionState.FilesQueued;
    }

    private void RenumberPages()
    {
        for (int i = 0; i < Pages.Count; i++)
            Pages[i].PageNumber = i + 1;
    }

    // ── Preview Commands ──────────────────────────────────────────────

    [RelayCommand]
    private void OpenPreview(PageThumbnailViewModel thumb)
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        PreviewPage = thumb;
    }

    [RelayCommand]
    private void ClosePreview()
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        PreviewPage = null;
    }

    // ── Merge Command ─────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task Merge()
    {
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;

        var prefs = _userSettings.Load();
        string initialDir = string.IsNullOrEmpty(prefs.DefaultSaveFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : prefs.DefaultSaveFolder;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Merged PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = "merged.pdf",
            InitialDirectory = initialDir
        };

        if (dialog.ShowDialog() != true) return; // user cancelled — no state change

        // Persist the directory
        prefs.DefaultSaveFolder = Path.GetDirectoryName(dialog.FileName) ?? initialDir;
        _userSettings.Save(prefs);

        SessionState = MergeSessionState.Merging;
        StatusMessage = "Building PDF…";

        try
        {
            var options = new PdfBuildOptions
            {
                Format = prefs.PdfFormat,
                JpegQuality = prefs.JpegQuality,
                PaperSize = PaperSize.AutoDetect  // each imported page keeps its own dimensions
            };
            await _pdfBuilder.BuildAsync(_pageData, options, dialog.FileName);
            SessionState = MergeSessionState.PagesLoaded;
            StatusMessage = $"Merged to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            SessionState = MergeSessionState.PagesLoaded;
            ErrorMessage = $"Merge failed: {ex.Message}";
        }
    }
    private bool CanMerge() =>
        SessionState == MergeSessionState.PagesLoaded && _pageData.Count > 0;

    // ── Discard Command ───────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void DiscardSession()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        FileQueue.Clear();
        Pages.Clear();
        _pageData.Clear();
        PreviewPage = null;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        IsLoadingPages = false;
        DeleteTempDir();
        SessionState = MergeSessionState.Idle;
    }
    private bool CanDiscard() => SessionState != MergeSessionState.Idle;

    // ── Helpers ───────────────────────────────────────────────────────

    private void DeleteTempDir()
    {
        if (!string.IsNullOrEmpty(_sessionTempDir) && Directory.Exists(_sessionTempDir))
        {
            try { Directory.Delete(_sessionTempDir, recursive: true); }
            catch { /* best-effort */ }
        }
        _sessionTempDir = string.Empty;
    }

    /// <summary>Exposed for testing — returns the current page order as IPageSource.</summary>
    public IReadOnlyList<ImportedPage> GetPageData() => _pageData.AsReadOnly();
}
