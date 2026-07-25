namespace PaperlessAiCore.Web.Services;

/// <summary>
/// Globaler Zustand für den aktiven Settings-Tab.
/// Wird vom MainLayout gesetzt (Sidebar-Klick) und von Settings.razor gelesen.
/// </summary>
public static class SettingsTabState
{
    public static string ActiveTab { get; set; } = "Verbindung & KI";
    public static event Action? OnChanged;
    public static void Set(string tab) { ActiveTab = tab; OnChanged?.Invoke(); }

    // Auto-Save Status: null=idle, "saving", "saved"
    public static string? SaveStatus { get; set; }
    public static event Action? OnSaveStatusChanged;
    public static void SetSaveStatus(string? status) { SaveStatus = status; OnSaveStatusChanged?.Invoke(); }
}
