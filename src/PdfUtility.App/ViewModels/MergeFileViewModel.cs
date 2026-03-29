// src/PdfUtility.App/ViewModels/MergeFileViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfUtility.App.ViewModels;

public partial class MergeFileViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
}
