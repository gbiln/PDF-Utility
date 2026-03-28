// src/PdfUtility.App/MainWindow.xaml.cs
using PdfUtility.App.ViewModels;
using Wpf.Ui.Controls;

namespace PdfUtility.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel vm, ScanDoubleSidedViewModel scanVm)
    {
        InitializeComponent();
        DataContext = vm;
        // ScanDoubleSidedView gets its VM via DI — see ScanDoubleSidedView.xaml.cs
    }
}
