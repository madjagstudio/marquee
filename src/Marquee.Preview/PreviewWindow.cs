using System.Drawing;
using System.Drawing.Drawing2D;
using SkiaSharp;

namespace Marquee.Preview;

/// Shows the latest frame scaled to fit, a red banner with the current layout error (if any), and a status line.
/// Press 1 to size the window so the frame shows at 1:1.
sealed class PreviewWindow : Form
{
    readonly SKSizeI _canvas;
    readonly FramePanel _view = new() { Dock = DockStyle.Fill };
    readonly Label _error = new()
    {
        Dock = DockStyle.Top,
        Visible = false,
        BackColor = Color.FromArgb(150, 28, 28),
        ForeColor = Color.White,
        Padding = new Padding(8),
        Font = new Font(FontFamily.GenericMonospace, 10),
    };
    readonly StatusStrip _statusStrip = new()
    {
        BackColor = SystemColors.Control,
        ForeColor = SystemColors.ControlText,
    };
    readonly ToolStripStatusLabel _status = new("Waiting for the first frame…")
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    Bitmap? _frame;

    public PreviewWindow(string title, SKSizeI canvas)
    {
        _canvas = canvas;
        Text = $"Marquee preview: {title}";
        BackColor = Color.Black;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        _statusStrip.Items.Add(_status);
        _view.Paint += PaintFrame;
        // Docking runs from the last control added to the first, so the fill view goes in first.
        Controls.Add(_view);
        Controls.Add(_error);
        Controls.Add(_statusStrip);

        ClientSize = new Size(canvas.Width / 2, canvas.Height / 2 + _statusStrip.Height);
    }

    public void ShowFrame(Bitmap frame)
    {
        var previous = _frame;
        _frame = frame;
        previous?.Dispose();
        _view.Invalidate();
    }

    public void ShowError(string? message)
    {
        _error.Text = message ?? "";
        _error.Visible = message is not null;
        if (message is not null)
            _error.Height = TextRenderer.MeasureText(message, _error.Font, new Size(ClientSize.Width - 16, int.MaxValue), TextFormatFlags.WordBreak).Height + 16;
    }

    public void ShowStatus(string text) => _status.Text = text;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.D1 or Keys.NumPad1)
        {
            var extra = _statusStrip.Height + (_error.Visible ? _error.Height : 0);
            var screen = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(Math.Min(_canvas.Width, screen.Width), Math.Min(_canvas.Height + extra, screen.Height));
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frame?.Dispose();
        base.Dispose(disposing);
    }

    void PaintFrame(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
        if (_frame is null) return;

        var area = _view.ClientRectangle;
        var scale = Math.Min((float)area.Width / _frame.Width, (float)area.Height / _frame.Height);
        var (w, h) = ((int)(_frame.Width * scale), (int)(_frame.Height * scale));
        e.Graphics.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        e.Graphics.DrawImage(_frame, new Rectangle((area.Width - w) / 2, (area.Height - h) / 2, w, h));
    }

    sealed class FramePanel : Panel
    {
        public FramePanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }
}
