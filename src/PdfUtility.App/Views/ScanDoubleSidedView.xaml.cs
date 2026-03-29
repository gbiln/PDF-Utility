// src/PdfUtility.App/Views/ScanDoubleSidedView.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using PdfUtility.App.ViewModels;

namespace PdfUtility.App.Views;

public partial class ScanDoubleSidedView : System.Windows.Controls.UserControl
{
    public ScanDoubleSidedView()
    {
        InitializeComponent();
        if (System.Windows.Application.Current is App app)
        {
            var vm = app.Services.GetRequiredService<ScanDoubleSidedViewModel>();
            DataContext = vm;
            Loaded += (_, _) => vm.RefreshDevicesCommand.Execute(null);
        }
    }
}
