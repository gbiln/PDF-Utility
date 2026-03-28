// src/PdfUtility.Core/Interfaces/IUserSettings.cs
using PdfUtility.Core.Models;

namespace PdfUtility.Core.Interfaces;

public interface IUserSettings
{
    UserPreferences Load();
    void Save(UserPreferences prefs);
}
