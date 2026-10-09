using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace IosInspector.Controls;

public enum IosRemoteAction
{
    Back,
    Home,
    AppSwitcher,
    Lock,
    VolumeDown,
    Mute,
    VolumeUp,
    Screenshot
}

public sealed class VncView : PictureBox
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Bitmap? _frame;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly HashSet<uint> _pressedKeys = [];
    private bool _pointerDown;
    public bool IsConnected => _client?.Connected == true;

    public VncView()
    {
        BackColor = Color.FromArgb(8, 11, 16);
        SizeMode = PictureBoxSizeMode.Zoom;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        MouseDown += async (_, e) => { Focus(); if (e.Button == MouseButtons.Left) { _pointerDown = true; await SendPointerAsync(e.Location, 1); } };
        MouseMove += async (_, e) => { if (_pointerDown) await SendPointerAsync(e.Location, 1); };
        MouseUp += async (_, e) => { if (e.Button == MouseButtons.Left) { _pointerDown = false; await SendPointerAsync(e.Location, 0); } };
        MouseWheel += async (_, e) => await SendPointerAsync(e.Location, e.Delta > 0 ? (byte)8 : (byte)16);
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        KeyPress += OnKeyPress;
        LostFocus += async (_, _) => await ReleasePressedKeysAsync();
    }

    public async Task ConnectAsync(string host, int port, CancellationToken token = default)
    {
        Disconnect();
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, token);
        _stream = _client.GetStream();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        await HandshakeAsync(_stream, _cts.Token);
        _ = ReceiveLoopAsync(_stream, _cts.Token);
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _cts?.Dispose();
        _cts = null; _stream = null; _client = null;
        _pressedKeys.Clear();
    }

    private async Task HandshakeAsync(NetworkStream stream, CancellationToken token)
    {
        var version = await ReadExactAsync(stream, 12, token);
        await stream.WriteAsync(version, token);
        var count = (await ReadExactAsync(stream, 1, token))[0];
        if (count == 0) throw new InvalidOperationException("VNC 서버가 연결을 거부했습니다.");
        var security = await ReadExactAsync(stream, count, token);
        if (!security.Contains((byte)1)) throw new InvalidOperationException("현재는 암호 없는 로컬 USB VNC 연결만 지원합니다.");
        await stream.WriteAsync(new byte[] { 1 }, token);
        var securityResult = BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(stream, 4, token));
        if (securityResult != 0) throw new InvalidOperationException("VNC 보안 협상에 실패했습니다.");
        await stream.WriteAsync(new byte[] { 1 }, token);

        var init = await ReadExactAsync(stream, 24, token);
        var width = BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(0, 2));
        var height = BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(2, 2));
        var nameLength = BinaryPrimitives.ReadUInt32BigEndian(init.AsSpan(20, 4));
        if (nameLength > 4096) throw new InvalidOperationException("잘못된 VNC 서버 정보입니다.");
        _ = await ReadExactAsync(stream, (int)nameLength, token);
        _frame = new Bitmap(width, height, PixelFormat.Format32bppRgb);

        byte[] pixelFormat = [0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0];
        await stream.WriteAsync(pixelFormat, token);
        byte[] encodings = [2, 0, 0, 1, 0, 0, 0, 0];
        await stream.WriteAsync(encodings, token);
        await RequestFrameAsync(false, token);
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var type = (await ReadExactAsync(stream, 1, token))[0];
                if (type == 0) await ReadFramebufferUpdateAsync(stream, token);
                else if (type == 2) { }
                else if (type == 3)
                {
                    var serverTextHeader = await ReadExactAsync(stream, 7, token);
                    var length = BinaryPrimitives.ReadInt32BigEndian(serverTextHeader.AsSpan(3, 4));
                    if (length == int.MinValue || Math.Abs((long)length) > 16 * 1024 * 1024)
                        throw new InvalidOperationException("VNC 서버 텍스트 데이터가 너무 큽니다.");
                    _ = await ReadExactAsync(stream, Math.Abs(length), token);
                }
                else throw new InvalidOperationException($"지원하지 않는 VNC 메시지: {type}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) BeginInvoke(() => { Disconnect(); BackColor = Color.DarkRed; Tag = ex.Message; }); }
    }

    private async Task ReadFramebufferUpdateAsync(NetworkStream stream, CancellationToken token)
    {
        var header = await ReadExactAsync(stream, 3, token);
        var rectangles = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1, 2));
        for (var i = 0; i < rectangles; i++)
        {
            var rect = await ReadExactAsync(stream, 12, token);
            var x = BinaryPrimitives.ReadUInt16BigEndian(rect.AsSpan(0, 2));
            var y = BinaryPrimitives.ReadUInt16BigEndian(rect.AsSpan(2, 2));
            var width = BinaryPrimitives.ReadUInt16BigEndian(rect.AsSpan(4, 2));
            var height = BinaryPrimitives.ReadUInt16BigEndian(rect.AsSpan(6, 2));
            var encoding = BinaryPrimitives.ReadInt32BigEndian(rect.AsSpan(8, 4));
            if (encoding != 0) throw new InvalidOperationException($"지원하지 않는 VNC 인코딩: {encoding}");
            var pixels = await ReadExactAsync(stream, width * height * 4, token);
            WritePixels(x, y, width, height, pixels);
        }
        if (_frame is not null && !IsDisposed)
        {
            var snapshot = new Bitmap(_frame);
            BeginInvoke(() => { var old = Image; Image = snapshot; old?.Dispose(); });
        }
        await RequestFrameAsync(true, token);
    }

    private void WritePixels(int x, int y, int width, int height, byte[] pixels)
    {
        if (_frame is null) return;
        var data = _frame.LockBits(new Rectangle(x, y, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (var row = 0; row < height; row++) Marshal.Copy(pixels, row * width * 4, data.Scan0 + row * data.Stride, width * 4);
        }
        finally { _frame.UnlockBits(data); }
    }

    private async Task RequestFrameAsync(bool incremental, CancellationToken token)
    {
        if (_stream is null || _frame is null) return;
        var message = new byte[10]; message[0] = 3; message[1] = incremental ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6, 2), (ushort)_frame.Width);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), (ushort)_frame.Height);
        await SendAsync(message, token);
    }

    private async Task SendPointerAsync(Point location, byte mask)
    {
        if (_stream is null || _frame is null || Image is null) return;
        var imageAspect = (double)_frame.Width / _frame.Height;
        var boxAspect = (double)ClientSize.Width / Math.Max(1, ClientSize.Height);
        var drawWidth = boxAspect > imageAspect ? ClientSize.Height * imageAspect : ClientSize.Width;
        var drawHeight = boxAspect > imageAspect ? ClientSize.Height : ClientSize.Width / imageAspect;
        var offsetX = (ClientSize.Width - drawWidth) / 2;
        var offsetY = (ClientSize.Height - drawHeight) / 2;
        var x = (ushort)Math.Clamp((location.X - offsetX) * _frame.Width / drawWidth, 0, _frame.Width - 1);
        var y = (ushort)Math.Clamp((location.Y - offsetY) * _frame.Height / drawHeight, 0, _frame.Height - 1);
        var message = new byte[6]; message[0] = 5; message[1] = mask;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), x);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4, 2), y);
        await SendAsync(message, _cts?.Token ?? CancellationToken.None);
        if (mask is 8 or 16) { message[1] = 0; await SendAsync(message, _cts?.Token ?? CancellationToken.None); }
    }

    public void FocusViewer()
    {
        if (!IsDisposed && CanFocus) Focus();
    }

    public async Task SendDeviceActionAsync(IosRemoteAction action)
    {
        if (!IsConnected) return;
        switch (action)
        {
            case IosRemoteAction.Back:
                await SendBackGestureAsync();
                break;
            case IosRemoteAction.Home:
                await PulsePointerButtonAsync(4);
                break;
            case IosRemoteAction.AppSwitcher:
                await PulsePointerButtonAsync(4);
                await Task.Delay(90);
                await PulsePointerButtonAsync(4);
                break;
            case IosRemoteAction.Lock:
                await PulsePointerButtonAsync(2);
                break;
            case IosRemoteAction.VolumeDown:
                await PulseKeyAsync(0x1008FF11);
                break;
            case IosRemoteAction.Mute:
                await PulseKeyAsync(0x1008FF12);
                break;
            case IosRemoteAction.VolumeUp:
                await PulseKeyAsync(0x1008FF13);
                break;
            case IosRemoteAction.Screenshot:
                await SendRawPointerAsync(2, FrameCenterX(), FrameCenterY());
                await SendKeyEventAsync(0x1008FF13, true);
                await Task.Delay(90);
                await SendKeyEventAsync(0x1008FF13, false);
                await SendRawPointerAsync(0, FrameCenterX(), FrameCenterY());
                break;
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsConnected) return;
        try
        {
            if (e.Control && e.KeyCode is Keys.C or Keys.V or Keys.X or Keys.A or Keys.Z)
            {
                e.SuppressKeyPress = true;
                await SendKeyEventAsync(0xFFE3, false); // Windows Ctrl을 iOS Command 단축키와 겹치지 않게 해제
                _pressedKeys.Remove(0xFFE3);
                await Task.Delay(60);
                await SendKeyEventAsync(0xFFE7, true);  // Meta_L = iOS Command
                var shortcut = (uint)char.ToLowerInvariant((char)e.KeyCode);
                await SendKeyEventAsync(shortcut, true);
                await Task.Delay(35);
                await SendKeyEventAsync(shortcut, false);
                await SendKeyEventAsync(0xFFE7, false);
                await ReleaseShortcutModifiersAsync();
                return;
            }

            var keysym = KeySymForKeyDown(e.KeyCode, e.Control || e.Alt);
            if (keysym is null || !_pressedKeys.Add(keysym.Value)) return;
            e.SuppressKeyPress = IsSpecialKey(e.KeyCode) || e.Control || e.Alt;
            await SendKeyEventAsync(keysym.Value, true);
        }
        catch { }
    }

    private async void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var keysym = KeySymForKeyDown(e.KeyCode, e.Control || e.Alt);
        if (keysym is null || !_pressedKeys.Remove(keysym.Value)) return;
        try { await SendKeyEventAsync(keysym.Value, false); } catch { }
    }

    private async void OnKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!IsConnected || char.IsControl(e.KeyChar)) return;
        e.Handled = true;
        var keysym = e.KeyChar <= 0xFF ? e.KeyChar : 0x01000000u | e.KeyChar;
        try
        {
            await SendKeyEventAsync(keysym, true);
            await SendKeyEventAsync(keysym, false);
        }
        catch { }
    }

    private async Task SendKeyEventAsync(uint keysym, bool down)
    {
        var message = new byte[8];
        message[0] = 4;
        message[1] = down ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4, 4), keysym);
        await SendAsync(message, _cts?.Token ?? CancellationToken.None);
    }

    private async Task PulseKeyAsync(uint keysym)
    {
        await SendKeyEventAsync(keysym, true);
        await Task.Delay(70);
        await SendKeyEventAsync(keysym, false);
    }

    private async Task PulsePointerButtonAsync(byte mask)
    {
        var x = FrameCenterX();
        var y = FrameCenterY();
        await SendRawPointerAsync(mask, x, y);
        await Task.Delay(70);
        await SendRawPointerAsync(0, x, y);
    }

    private async Task SendBackGestureAsync()
    {
        if (_frame is null) return;
        var y = (ushort)(_frame.Height / 2);
        var startX = (ushort)Math.Max(1, _frame.Width / 100);
        var endX = (ushort)(_frame.Width * 2 / 3);
        await SendRawPointerAsync(1, startX, y);
        for (var step = 1; step <= 10; step++)
        {
            var x = (ushort)(startX + (endX - startX) * step / 10);
            await SendRawPointerAsync(1, x, y);
            await Task.Delay(12);
        }
        await SendRawPointerAsync(0, endX, y);
    }

    private ushort FrameCenterX() => (ushort)Math.Max(0, (_frame?.Width ?? 1) / 2);
    private ushort FrameCenterY() => (ushort)Math.Max(0, (_frame?.Height ?? 1) / 2);

    private async Task SendRawPointerAsync(byte mask, ushort x, ushort y)
    {
        var message = new byte[6];
        message[0] = 5;
        message[1] = mask;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), x);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4, 2), y);
        await SendAsync(message, _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReleasePressedKeysAsync()
    {
        foreach (var keysym in _pressedKeys.ToArray())
        {
            try { await SendKeyEventAsync(keysym, false); } catch { }
        }
        _pressedKeys.Clear();
    }

    private async Task ReleaseShortcutModifiersAsync()
    {
        uint[] modifiers = [0xFFE1, 0xFFE2, 0xFFE3, 0xFFE4, 0xFFE7, 0xFFE8, 0xFFE9, 0xFFEA, 0xFFEB, 0xFFEC];
        foreach (var keysym in modifiers)
        {
            try { await SendKeyEventAsync(keysym, false); } catch { }
            _pressedKeys.Remove(keysym);
        }
    }

    private static uint? KeySymForKeyDown(Keys key, bool modified) => key switch
    {
        Keys.Back => 0xFF08, Keys.Tab => 0xFF09, Keys.Return => 0xFF0D, Keys.Escape => 0xFF1B,
        Keys.Home => 0xFF50, Keys.Left => 0xFF51, Keys.Up => 0xFF52, Keys.Right => 0xFF53,
        Keys.Down => 0xFF54, Keys.PageUp => 0xFF55, Keys.PageDown => 0xFF56, Keys.End => 0xFF57,
        Keys.Insert => 0xFF63, Keys.Delete => 0xFFFF,
        Keys.F1 => 0xFFBE, Keys.F2 => 0xFFBF, Keys.F3 => 0xFFC0, Keys.F4 => 0xFFC1,
        Keys.F5 => 0xFFC2, Keys.F6 => 0xFFC3, Keys.F7 => 0xFFC4, Keys.F8 => 0xFFC5,
        Keys.F9 => 0xFFC6, Keys.F10 => 0xFFC7, Keys.F11 => 0xFFC8, Keys.F12 => 0xFFC9,
        Keys.ShiftKey or Keys.LShiftKey => 0xFFE1, Keys.RShiftKey => 0xFFE2,
        Keys.ControlKey or Keys.LControlKey => 0xFFE3, Keys.RControlKey => 0xFFE4,
        Keys.Menu or Keys.LMenu => 0xFFE9, Keys.RMenu => 0xFFEA,
        >= Keys.A and <= Keys.Z when modified => (uint)char.ToLowerInvariant((char)key),
        >= Keys.D0 and <= Keys.D9 when modified => (uint)('0' + key - Keys.D0),
        _ => null
    };

    private static bool IsSpecialKey(Keys key) => KeySymForKeyDown(key, false) is not null;

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return IsSpecialKey(key) || base.IsInputKey(keyData);
    }

    private async Task SendAsync(byte[] data, CancellationToken token)
    {
        if (_stream is null) return;
        await _sendLock.WaitAsync(token);
        try { await _stream.WriteAsync(data, token); }
        finally { _sendLock.Release(); }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken token)
    {
        var buffer = new byte[length]; var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), token);
            if (read == 0) throw new EndOfStreamException("VNC 연결이 종료됐습니다.");
            offset += read;
        }
        return buffer;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Disconnect(); _frame?.Dispose(); _sendLock.Dispose(); }
        base.Dispose(disposing);
    }
}
