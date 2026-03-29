// src/PdfUtility.App/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfUtility.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedTabIndex = 0;
}
