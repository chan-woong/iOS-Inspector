using System.Net;

namespace IosInspector.Controls;

public sealed class MjpegView : PictureBox
{
    private CancellationTokenSource? _cts;
    public bool IsRunning => _cts is not null;

    public MjpegView()
    {
        BackColor = Color.FromArgb(14, 17, 23);
        SizeMode = PictureBoxSizeMode.Zoom;
    }

    public async Task StartAsync(string url)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[128 * 1024];
            var frame = new MemoryStream();
            int previous = -1;
            while (!token.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, token);
                if (count == 0) break;
                for (var i = 0; i < count; i++)
                {
                    var current = buffer[i];
                    frame.WriteByte(current);
                    if (previous == 0xFF && current == 0xD9)
                    {
                        var bytes = frame.ToArray();
                        var start = Array.FindIndex(bytes, b => b == 0xFF);
                        if (start >= 0 && start + 1 < bytes.Length && bytes[start + 1] == 0xD8)
                        {
                            using var source = new MemoryStream(bytes, start, bytes.Length - start);
                            using var decoded = Image.FromStream(source);
                            var image = new Bitmap(decoded);
                            BeginInvoke(() => { var old = Image; Image = image; old?.Dispose(); });
                        }
                        frame.Dispose();
                        frame = new MemoryStream();
                    }
                    previous = current;
                }
            }
            frame.Dispose();
        }
        finally
        {
            if (_cts?.Token == token) _cts = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Stop();
        base.Dispose(disposing);
    }
}
