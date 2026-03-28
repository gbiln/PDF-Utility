// src/PdfUtility.App/MainWindow.xaml.cs
using PdfUtility.App.ViewModels;
using Wpf.Ui.Controls;

namespace PdfUtility.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // ScanDoubleSidedView DataContext will be set in Task 10
    }
}
