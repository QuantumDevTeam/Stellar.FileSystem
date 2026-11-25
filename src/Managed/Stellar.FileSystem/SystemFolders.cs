using System.Runtime.InteropServices;

namespace Stellar.FileSystem;

/// <summary>
/// The folders of the machine and its user, as full paths: sources of domains. A game names
/// <see cref="Kernel.FileSystem.Folders"/> instead.
/// </summary>
/// <remarks>
/// Facts of the machine, not of an engine instance, so they are static; nothing is kept, safe from any thread. A
/// folder is told even if it is not there yet.
/// </remarks>
public static class SystemFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid SavedGamesId = new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");
    private static readonly Guid LocalLowId = new("A520A1A4-1780-4FF6-BD18-167343C5AF16");

    /// <summary>Gets the folder of the application.</summary>
    public static string App => AppContext.BaseDirectory;

    /// <summary>Gets the folder for temporary files.</summary>
    public static string Temp => Path.GetTempPath();

    /// <summary>Gets the folder of the user.</summary>
    public static string Home => Get(Environment.SpecialFolder.UserProfile);

    /// <summary>Gets the desktop of the user.</summary>
    public static string Desktop => Get(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Gets the documents of the user.</summary>
    public static string Documents => Get(Environment.SpecialFolder.MyDocuments);

    /// <summary>Gets the downloads of the user.</summary>
    public static string Downloads => GetKnown(DownloadsId, Path.Combine(Home, "Downloads"));

    /// <summary>Gets the pictures of the user.</summary>
    public static string Pictures => Get(Environment.SpecialFolder.MyPictures);

    /// <summary>Gets the music of the user.</summary>
    public static string Music => Get(Environment.SpecialFolder.MyMusic);

    /// <summary>Gets the videos of the user.</summary>
    public static string Videos => Get(Environment.SpecialFolder.MyVideos);

    /// <summary>Gets the saved games of the user; on other systems than Windows, the local data.</summary>
    public static string SavedGames => GetKnown(SavedGamesId, AppData.Local);

    /// <summary>Gets the data all users of the machine share.</summary>
    public static string Shared => Get(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>
    /// The data of applications, for this user.
    /// </summary>
    public static class AppData
    {
        /// <summary>Gets the data that follows the user from machine to machine.</summary>
        public static string Roaming => Get(Environment.SpecialFolder.ApplicationData);

        /// <summary>Gets the data of this machine.</summary>
        public static string Local => Get(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>
        /// Gets the data of this machine a process with few rights may write; on other systems than Windows, the
        /// same as <see cref="Local"/>.
        /// </summary>
        public static string LocalLow => GetKnown(LocalLowId, Local);
    }

    private static string Get(Environment.SpecialFolder folder)
    {
        return Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    // The folders .NET has no name for. The user may have moved them, so Windows is asked
    private static string GetKnown(Guid id, string fallback)
    {
        if (!OperatingSystem.IsWindows()) return fallback;

        const uint doNotVerify = 0x4000;
        int result = SHGetKnownFolderPath(in id, doNotVerify, IntPtr.Zero, out IntPtr text);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(text) ?? fallback : fallback;
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }
}
