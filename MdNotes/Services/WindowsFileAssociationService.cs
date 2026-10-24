using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MdNotes.Services;

/// <summary>
/// Registers MdNotes under HKEY_CURRENT_USER, so no installer and no admin rights are needed.
/// Windows protects the actual default with a hashed UserChoice key that only the user can set,
/// so this makes MdNotes available and then opens the Default apps page for them to confirm.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileAssociationService : IFileAssociationService
{
    private const string ProgId = "MdNotes.md";
    private const string Extension = ".md";
    private const string AppName = "MdNotes";

    private const int ShcneAssocChanged = 0x08000000;
    private const int ShcnfIdList = 0x0000;

    public bool IsSupported => true;

    public bool IsDefault =>
        Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{Extension}\UserChoice")
            ?.GetValue("ProgId") as string == ProgId;

    public void Register()
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Cannot find the MdNotes executable to register.");
        var command = $"\"{exe}\" \"%1\"";
        var classes = $@"Software\Classes";

        Set($@"{classes}\{ProgId}", null, "Markdown Document");
        Set($@"{classes}\{ProgId}\DefaultIcon", null, $"\"{exe}\",0");
        Set($@"{classes}\{ProgId}\shell\open\command", null, command);

        // Puts MdNotes in the "Open with" list for .md without touching the current default.
        Set($@"{classes}\{Extension}\OpenWithProgids", ProgId, string.Empty);

        var exeName = Path.GetFileName(exe);
        Set($@"{classes}\Applications\{exeName}\shell\open\command", null, command);
        Set($@"{classes}\Applications\{exeName}\SupportedTypes", Extension, string.Empty);

        // Capabilities are what list MdNotes on the Settings > Default apps page.
        Set($@"Software\{AppName}\Capabilities", "ApplicationName", AppName);
        Set($@"Software\{AppName}\Capabilities", "ApplicationDescription", "Markdown notes and files");
        Set($@"Software\{AppName}\Capabilities\FileAssociations", Extension, ProgId);
        Set(@"Software\RegisteredApplications", AppName, $@"Software\{AppName}\Capabilities");

        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

        Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppName={AppName}")
        {
            UseShellExecute = true,
        });
    }

    private static void Set(string subKey, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey)
                        ?? throw new InvalidOperationException($"Cannot write the registry key: {subKey}");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
