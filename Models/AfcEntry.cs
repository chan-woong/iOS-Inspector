namespace IosInspector.Models;

public sealed record AfcEntry(string Name, string FullPath, bool IsDirectory, long? Size)
{
    public string TypeText => IsDirectory ? "폴더" : "파일";
    public string SizeText => IsDirectory || Size is null ? "" : FormatSize(Size.Value);

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
