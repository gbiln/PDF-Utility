// src/PdfUtility.Core/Interfaces/IUserSettings.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IUserSettings
{
    /// <summary>Loads user preferences from persistent storage. Returns defaults if no saved preferences exist.</summary>
    UserPreferences Load();

    /// <summary>Saves user preferences to persistent storage.</summary>
    void Save(UserPreferences prefs);
}
