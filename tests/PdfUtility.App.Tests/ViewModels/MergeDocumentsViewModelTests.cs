// tests/PdfUtility.App.Tests/ViewModels/MergeDocumentsViewModelTests.cs
using PdfUtility.App.Tests.Fakes;
using PdfUtility.App.ViewModels;
using PdfUtility.Core.Exceptions;
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Tests.ViewModels;

public class MergeDocumentsViewModelTests
{
    private sealed class FakePdfBuilder : IPdfBuilder
    {
        public List<IPageSource> LastPages { get; private set; } = [];
        public bool ShouldFail { get; set; }

        public Task BuildAsync(IEnumerable<IPageSource> pages, PdfBuildOptions options, string outputPath)
        {
            LastPages = pages.ToList();
            if (ShouldFail) throw new IOException("Fake write error");
            return Task.CompletedTask;
        }
    }

    private static MergeDocumentsViewModel CreateVm(
        FakePdfImporter? importer = null,
        FakePdfBuilder? builder = null)
    {
        return new MergeDocumentsViewModel(
            importer ?? new FakePdfImporter(),
            builder ?? new FakePdfBuilder(),
            new PdfUtility.App.Services.InMemoryUserSettings());
    }

    private static ImportedPage MakePage(string path, int idx = 0) =>
        new ImportedPage(path, idx, "file.pdf");

    // ── Initial state ─────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsIdle_NoCommands()
    {
        var vm = CreateVm();
        Assert.Equal(MergeSessionState.Idle, vm.SessionState);
        // AddFilesCommand uses CanModifyQueue which is true for Idle
        Assert.True(vm.AddFilesCommand.CanExecute(null));
        Assert.False(vm.LoadPagesCommand.CanExecute(null));
        Assert.False(vm.MergeCommand.CanExecute(null));
        Assert.False(vm.DiscardSessionCommand.CanExecute(null));
    }

    // ── AddFiles ──────────────────────────────────────────────────────

    [Fact]
    public void AddFiles_PopulatesFileQueue_TransitionsToFilesQueued()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "a.pdf", "b.pdf" });
        Assert.Equal(MergeSessionState.FilesQueued, vm.SessionState);
        Assert.Equal(2, vm.FileQueue.Count);
        Assert.True(vm.LoadPagesCommand.CanExecute(null));
    }

    // ── RemoveFile ────────────────────────────────────────────────────

    [Fact]
    public void RemoveFile_LastFile_TransitionsToIdle()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        vm.RemoveFileCommand.Execute(vm.FileQueue[0]);
        Assert.Equal(MergeSessionState.Idle, vm.SessionState);
        Assert.Empty(vm.FileQueue);
    }

    // ── LoadPages ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadPages_PopulatesPages_TransitionsToPagesLoaded()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0), MakePage("p2.png", 1)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });

        await vm.LoadPagesCommand.ExecuteAsync(null);

        Assert.Equal(MergeSessionState.PagesLoaded, vm.SessionState);
        Assert.Equal(2, vm.Pages.Count);
        Assert.Equal(2, vm.GetPageData().Count);
        Assert.Equal("p1.png", vm.GetPageData()[0].ImagePath);
        Assert.Equal("p2.png", vm.GetPageData()[1].ImagePath);
    }

    [Fact]
    public async Task LoadPages_AllImportsFail_ShowsError_StaysInFilesQueued()
    {
        var importer = new FakePdfImporter();
        importer.NextImportError = new PdfImportException("bad pdf");
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "bad.pdf" });

        await vm.LoadPagesCommand.ExecuteAsync(null);

        Assert.Equal(MergeSessionState.FilesQueued, vm.SessionState);
        Assert.Empty(vm.Pages);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Contains("No pages could be loaded", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadPages_OneFileFailsOneSucceeds_LoadsPartialPages_ShowsError()
    {
        var importer = new FakePdfImporter();
        importer.NextImportError = new PdfImportException("file1 bad");
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "bad.pdf", "good.pdf" });

        await vm.LoadPagesCommand.ExecuteAsync(null);

        Assert.Equal(MergeSessionState.PagesLoaded, vm.SessionState);
        Assert.Single(vm.Pages);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Contains("Some files could not be imported", vm.ErrorMessage);
    }

    // ── Page order ────────────────────────────────────────────────────

    [Fact]
    public async Task MovePageToBeginning_MovesPageToFront()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0), MakePage("p2.png", 1), MakePage("p3.png", 2)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);

        vm.MovePageToBeginningCommand.Execute(vm.Pages[2]); // move p3 to front

        Assert.Equal("p3.png", vm.GetPageData()[0].ImagePath);
        Assert.Equal("p1.png", vm.GetPageData()[1].ImagePath);
        Assert.Equal("p2.png", vm.GetPageData()[2].ImagePath);
    }

    [Fact]
    public async Task MovePageToEnd_MovesPageToBack()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0), MakePage("p2.png", 1), MakePage("p3.png", 2)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);

        vm.MovePageToEndCommand.Execute(vm.Pages[0]); // move p1 to end

        Assert.Equal("p2.png", vm.GetPageData()[0].ImagePath);
        Assert.Equal("p3.png", vm.GetPageData()[1].ImagePath);
        Assert.Equal("p1.png", vm.GetPageData()[2].ImagePath);
    }

    [Fact]
    public async Task RemovePage_RemovesFromPagesAndPageData()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0), MakePage("p2.png", 1)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);

        vm.RemovePageCommand.Execute(vm.Pages[0]); // remove p1

        Assert.Single(vm.Pages);
        Assert.Equal("p2.png", vm.GetPageData()[0].ImagePath);
        Assert.Equal(1, vm.Pages[0].PageNumber); // renumbered
    }

    // ── Merge page order ──────────────────────────────────────────────

    [Fact]
    public async Task MovePageToBeginning_PageDataOrderMatchesPageOrder()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0), MakePage("p2.png", 1)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);

        // Swap pages
        vm.MovePageToBeginningCommand.Execute(vm.Pages[1]); // p2 first

        Assert.Equal("p2.png", vm.GetPageData()[0].ImagePath);
        Assert.Equal("p1.png", vm.GetPageData()[1].ImagePath);
    }

    // ── Discard ───────────────────────────────────────────────────────

    [Fact]
    public async Task DiscardSession_ClearsEverything_ReturnToIdle()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);
        Assert.Equal(MergeSessionState.PagesLoaded, vm.SessionState);

        vm.DiscardSessionCommand.Execute(null);

        Assert.Equal(MergeSessionState.Idle, vm.SessionState);
        Assert.Empty(vm.FileQueue);
        Assert.Empty(vm.Pages);
        Assert.Empty(vm.GetPageData());
    }

    // ── AddFiles enabled states ───────────────────────────────────────

    [Fact]
    public void AddFilesCommand_EnabledInIdleAndFilesQueued()
    {
        var vm = CreateVm();
        // Idle: CanModifyQueue returns true for Idle
        Assert.True(vm.AddFilesCommand.CanExecute(null));
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        // FilesQueued: also allowed
        Assert.True(vm.AddFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddFilesCommand_EnabledInPagesLoaded()
    {
        var importer = new FakePdfImporter();
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0)]);
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "a.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);
        Assert.Equal(MergeSessionState.PagesLoaded, vm.SessionState);

        Assert.True(vm.AddFilesCommand.CanExecute(null));
    }

    // ── StatusMessage and ErrorMessage clearing ───────────────────────

    [Fact]
    public async Task LoadPages_ClearsStatusAndErrorFromPreviousOperation()
    {
        var importer = new FakePdfImporter();
        // First load fails
        importer.NextImportError = new PdfImportException("fail");
        var vm = CreateVm(importer);
        vm.AddFilesCommand.Execute(new[] { "bad.pdf" });
        await vm.LoadPagesCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ErrorMessage);

        // Second load succeeds
        importer.ImportQueue.Enqueue([MakePage("p1.png", 0)]);
        await vm.LoadPagesCommand.ExecuteAsync(null);

        Assert.Empty(vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusMessage);
    }
}
