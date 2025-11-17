using System.Runtime.InteropServices;

namespace LolManager.Models;

public class SystemInfo
{
    public string OS { get; set; } = string.Empty;
    public string Runtime { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;

    public SystemInfo()
    {
        LoadSystemInfo();
    }

    private void LoadSystemInfo()
    {
        try
        {
            OS = $"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}";
            Runtime = RuntimeInformation.FrameworkDescription;
            Architecture = RuntimeInformation.OSArchitecture.ToString();
        }
        catch
        {
            OS = "Не удалось получить информацию";
            Runtime = "Не удалось получить информацию";
            Architecture = "Не удалось получить информацию";
        }
    }
}
