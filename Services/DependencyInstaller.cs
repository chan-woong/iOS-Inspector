using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IosInspector.Services;

public sealed record DependencyStatus(IReadOnlyList<string> MissingCore, bool ZsignMissing)
{
    public bool CoreReady => MissingCore.Count == 0;
    public bool AllReady => CoreReady && !ZsignMissing;
}

public sealed class DependencyInstaller
{
    private static readonly string[] CoreTools =
        ["idevice_id", "ideviceinfo", "ideviceinstaller", "afcclient", "iproxy"];

    private readonly HttpClient _http = new();
    public string ToolDirectory { get; }

    public DependencyInstaller(string toolDirectory)
    {
        ToolDirectory = toolDirectory;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IosInspector", "0.1"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public DependencyStatus GetStatus()
    {
        var missing = CoreTools.Where(name => !Exists(name)).ToArray();
        return new DependencyStatus(missing, !Exists("zsign"));
    }

    public async Task InstallAllAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(ToolDirectory);
        var status = GetStatus();
        if (!status.CoreReady)
        {
            progress?.Report("libimobiledevice Windows 도구 다운로드 중…");
            await InstallLatestReleaseAsync(
                "jrjr/libimobiledevice-windows",
                name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        (name.Contains("x86_64", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("win64", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("w64", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("windows", StringComparison.OrdinalIgnoreCase)),
                progress, token);
        }

        if (!Exists("ideviceinstaller"))
        {
            progress?.Report("ideviceinstaller Windows 도구 다운로드 중…");
            await InstallLatestReleaseAsync(
                "L1ghtmann/libimobiledevice",
                name => name.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("x86_64", StringComparison.OrdinalIgnoreCase),
                progress, token);
        }

        if (status.ZsignMissing)
        {
            progress?.Report("zsign Windows 도구 다운로드 중…");
            await InstallLatestReleaseAsync(
                "zhlynn/zsign",
                name => (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) &&
                        name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                        (name.Contains("x64", StringComparison.OrdinalIgnoreCase) || name.Contains("x86_64", StringComparison.OrdinalIgnoreCase)),
                progress, token);
        }

        var after = GetStatus();
        if (!after.CoreReady)
            throw new InvalidOperationException("다운로드 파일에서 필수 도구를 모두 찾지 못했습니다: " + string.Join(", ", after.MissingCore));
    }

    public async Task<string> DownloadLatestAssetAsync(string repository, Func<string, bool> assetFilter,
        string destinationDirectory, CancellationToken token = default)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", token);
        response.EnsureSuccessStatusCode();
        await using var jsonStream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: token);
        var asset = document.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item => assetFilter(item.GetProperty("name").GetString() ?? ""));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"{repository} 최신 릴리스에서 필요한 파일을 찾지 못했습니다.");
        Directory.CreateDirectory(destinationDirectory);
        var name = asset.GetProperty("name").GetString()!;
        var destination = Path.Combine(destinationDirectory, name);
        await using var input = await _http.GetStreamAsync(asset.GetProperty("browser_download_url").GetString()!, token);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, token);
        return destination;
    }

    private bool Exists(string name) => File.Exists(Path.Combine(ToolDirectory, name + ".exe"));

    private async Task InstallLatestReleaseAsync(string repository, Func<string, bool> assetFilter,
        IProgress<string>? progress, CancellationToken token)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", token);
        response.EnsureSuccessStatusCode();
        await using var jsonStream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(jsonStream, cancellationToken: token);
        var assets = document.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var asset = assets.FirstOrDefault(item => assetFilter(item.GetProperty("name").GetString() ?? ""));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"{repository}의 최신 릴리스에서 Windows x64 파일을 찾지 못했습니다.");

        var assetName = asset.GetProperty("name").GetString()!;
        var url = asset.GetProperty("browser_download_url").GetString()!;
        progress?.Report($"{assetName} 받는 중…");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "IosInspector", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var archivePath = Path.Combine(tempDirectory, assetName);
            await using (var input = await _http.GetStreamAsync(url, token))
            await using (var output = File.Create(archivePath))
                await input.CopyToAsync(output, token);

            var extractDirectory = Path.Combine(tempDirectory, "extracted");
            Directory.CreateDirectory(extractDirectory);
            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, extractDirectory, true);
            }
            else
            {
                await ExtractTarAsync(archivePath, extractDirectory, token);
            }

            progress?.Report($"{repository} 설치 중…");
            foreach (var file in Directory.EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                File.Copy(file, Path.Combine(ToolDirectory, Path.GetFileName(file)), true);
        }
        finally
        {
            try { Directory.Delete(tempDirectory, true); } catch { /* 임시 파일은 다음 OS 정리 때 제거 */ }
        }
    }

    private static async Task ExtractTarAsync(string archive, string destination, CancellationToken token)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "tar.exe", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-xf");
        startInfo.ArgumentList.Add(archive);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(destination);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows tar.exe를 실행할 수 없습니다.");
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
    }
}
