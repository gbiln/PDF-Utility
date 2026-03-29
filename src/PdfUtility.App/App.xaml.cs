// src/PdfUtility.App/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfUtility.App.Services;
using PdfUtility.App.ViewModels;
using PdfUtility.Core.Interfaces;
using PdfUtility.Pdf;
using PdfUtility.Scanning;
using System.IO;
using System.Windows;

namespace PdfUtility.App;

public partial class App : Application
{
    private IHost? _host;
    public IServiceProvider Services => _host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IUserSettings, InMemoryUserSettings>();
                services.AddSingleton<IScannerBackend, Naps2ScannerBackend>();
                services.AddSingleton<IPdfBuilder, PdfSharpPdfBuilder>();
                services.AddSingleton<IPdfImporter, Naps2PdfImporter>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<ScanDoubleSidedViewModel>();
                services.AddSingleton<MergeDocumentsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Pre-warm scanner on background thread
        var scanner = _host.Services.GetRequiredService<IScannerBackend>();
        _ = Task.Run(async () =>
        {
            try { await scanner.InitialiseAsync(); }
            catch { /* scanner not connected at startup — that's fine */ }
        });

        // Clean up orphaned merge temp directories (best-effort)
        _ = Task.Run(CleanupOrphanedMergeTempDirs);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private static void CleanupOrphanedMergeTempDirs()
    {
        try
        {
            var tempRoot = Path.GetTempPath();
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var dir in Directory.GetDirectories(tempRoot, "PdfUtility_Merge_*"))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (info.CreationTimeUtc < cutoff)
                        info.Delete(recursive: true);
                }
                catch { /* skip locked or inaccessible dirs */ }
            }
        }
        catch { /* ignore any top-level failure */ }
    }
}
