namespace IosInspector.Models;

public sealed record DeviceInfo(string Udid, string Name, string ProductType, string ProductVersion)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Udid : $"{Name}  ·  iOS {ProductVersion}";
}
