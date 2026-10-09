namespace IosInspector.Controls;

/// <summary>
/// TabControl leaves the unused part of its native header in the Windows theme
/// color. Paint that area after the native control so dark application themes do
/// not end in a white strip to the right of the tabs.
/// </summary>
public sealed class ThemedTabControl : TabControl
{
    private const int WmPaint = 0x000F;
    private Color _headerBackColor = Color.FromArgb(20, 29, 46);

    public Color HeaderBackColor
    {
        get => _headerBackColor;
        set
        {
            _headerBackColor = value;
            Invalidate();
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);

        if (message.Msg == WmPaint && IsHandleCreated)
            PaintUnusedHeader();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    private void PaintUnusedHeader()
    {
        var headerHeight = Math.Max(DisplayRectangle.Top, ItemSize.Height);
        var occupiedRight = TabCount > 0 ? GetTabRect(TabCount - 1).Right : 0;
        var tabBottom = TabCount > 0 ? GetTabRect(0).Bottom : 0;

        using var graphics = CreateGraphics();
        using var brush = new SolidBrush(HeaderBackColor);

        if (occupiedRight < ClientSize.Width)
            graphics.FillRectangle(brush, occupiedRight, 0, ClientSize.Width - occupiedRight, headerHeight);

        if (tabBottom < headerHeight)
            graphics.FillRectangle(brush, 0, tabBottom, ClientSize.Width, headerHeight - tabBottom);
    }
}
