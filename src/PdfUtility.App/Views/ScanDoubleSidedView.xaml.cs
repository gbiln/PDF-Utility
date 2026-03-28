// src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using PdfUtility.App.ViewModels;

namespace PdfUtility.App.Views;

public partial class ScanDoubleSidedView : System.Windows.Controls.UserControl
{
    public ScanDoubleSidedView()
    {
        InitializeComponent();
        // Resolve ViewModel from DI when running in the real app
        if (System.Windows.Application.Current is App app)
            DataContext = app.Services.GetRequiredService<ScanDoubleSidedViewModel>();
    }
}
