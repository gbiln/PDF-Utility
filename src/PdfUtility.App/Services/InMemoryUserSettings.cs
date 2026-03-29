// src/PdfUtility.App/Services/InMemoryUserSettings.cs
using PdfUtility.Core.Interfaces;
using PdfUtility.Core.Models;

namespace PdfUtility.App.Services;

public class InMemoryUserSettings : IUserSettings
{
    private UserPreferences _prefs = new();
    public UserPreferences Load() => _prefs;
    public void Save(UserPreferences prefs) => _prefs = prefs;
}
