namespace IosInspector.Models;

public sealed class AppSettings
{
    public string SshHost { get; set; } = "192.168.0.1";
    public int SshPort { get; set; } = 22;
    public int UsbLocalPort { get; set; } = 2222;
    public string SshUser { get; set; } = "root";
}
