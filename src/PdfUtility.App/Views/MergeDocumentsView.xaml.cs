// src/PdfUtility.App/Views/MergeDocumentsView.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PdfUtility.App.ViewModels;

namespace PdfUtility.App.Views;

public partial class MergeDocumentsView : System.Windows.Controls.UserControl
{
    public MergeDocumentsView()
    {
        InitializeComponent();
        if (System.Windows.Application.Current is App app)
            DataContext = app.Services.GetRequiredService<MergeDocumentsViewModel>();
    }

    private void AddFilesButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not MergeDocumentsViewModel vm) return;

        var dialog = new OpenFileDialog
        {
            Title = "Select PDF Files",
            Filter = "PDF Files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
            vm.AddFilesCommand.Execute(dialog.FileNames);
    }
}
