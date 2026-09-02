using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

internal sealed class InputPrompt : IbForm
{
    private const int Pad = 28;
    private const int Header = 48;
    private const int Dot = 11;
    private const int Mark = 52;
    private const int WellH = 72;
    private const int WellGap = 14;
    private const int BtnW = 112;
    private const int BtnH = 40;

    private readonly GlassField _real;
    private readonly GlassField _fake;
    private readonly Image _logo;
    private readonly Font _fBrand;
    private readonly Font _fTitle;
    private readonly Font _fMeta;
    private readonly Font _fBtn;
    private readonly Font _fNav;
    private readonly string _kind;
    private readonly string _kindTag;
    private readonly string _heading;
    private readonly string _sub;

    private Bitmap _frost;
    private int _bmpW, _bmpH;
    private Rectangle _rClose, _rMark, _rReal, _rFake, _rOk, _rCancel;
    private int _hot = -1;
    private bool _pressed;

    public string Value { get { return _real.Box.Text; } }
    public string Fake { get { return _fake.Box.Text; } }

    public InputPrompt(string kind)
    {
        _kind = (kind ?? "email").ToLowerInvariant();
        if (_kind == "phone")
        {
            _kindTag = "PHONE";
            _heading = "Hide a phone";
            _sub = "This number never appears on stream or recordings.";
        }
        else if (_kind == "custom")
        {
            _kindTag = "CUSTOM";
            _heading = "Hide custom text";
            _sub = "A name, handle, or any other text you want masked.";
        }
        else
        {
            _kind = "email";
            _kindTag = "EMAIL";
            _heading = "Hide an email";
            _sub = "This address never appears on stream or recordings.";
        }

        Text = "IntelByte";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(480, 456);
        Font = new Font("Segoe UI", 9.5f);
        var ico = Program.LoadIconFull();
        if (ico != null) Icon = ico;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoValidate = AutoValidate.Disable;
        CausesValidation = false;

        _logo = Program.LoadLogo();
        _fBrand = Program.LoadDetectiveFont(11f);
        _fTitle = Program.LoadDetectiveFont(20f);
        _fMeta = new Font("Segoe UI", 9.5f);
        _fBtn = new Font("Segoe UI Semibold", 11f);
        _fNav = new Font("Segoe UI Semibold", 8.5f);

        _real = MakeField(_kind == "phone" ? "+1 555 0100" : _kind == "custom" ? "a name, handle, or any text" : "name@company.com", "VALUE", null);
        _fake = MakeField("leave empty for a random mask", "SHOW INSTEAD", "optional");
        Controls.Add(_real);
        Controls.Add(_fake);

        KeyDown += OnKeys;
        Shown += delegate
        {
            RebuildFrost();
            LayoutFields();
            _real.Box.Focus();
            Invalidate();
        };
        SizeChanged += delegate
        {
            if (IsDisposed || Disposing) return;
            RebuildFrost();
            LayoutFields();
            Invalidate();
        };
        StreamCapture.HideFromCapture(this);
    }

    private GlassField MakeField(string ghost, string kicker, string optional)
    {
        var box = new GlassField(ghost)
        {
            Kicker = kicker,
            Optional = optional,
        };
        box.Box.Font = new Font("Segoe UI", 12.5f);
        box.Box.Visible = true;
        box.Box.GotFocus += delegate { if (!IsDisposed && !Disposing) Invalidate(); };
        box.Box.LostFocus += delegate { if (!IsDisposed && !Disposing) Invalidate(); };
        return box;
    }

    private void LayoutFields()
    {
        var yDot = (Header - Dot) / 2;
        _rClose = new Rectangle(Pad, yDot, Dot, Dot);

        var top = Header + Pad;
        _rMark = new Rectangle(Pad, top, Mark, Mark);

        var wellsTop = _rMark.Bottom + 28;
        _rReal = new Rectangle(Pad, wellsTop, Width - Pad * 2, WellH);
        _rFake = new Rectangle(Pad, _rReal.Bottom + WellGap, Width - Pad * 2, WellH);

        _rOk = new Rectangle(Width - Pad - BtnW, Height - Pad - BtnH, BtnW, BtnH);
        _rCancel = new Rectangle(_rOk.X - 20 - 88, _rOk.Y, 88, BtnH);

        _real.Bounds = _rReal;
        _fake.Bounds = _rFake;
    }

    private void RebuildFrost()
    {
        if (IsDisposed || Disposing || Width < 8 || Height < 8) return;
        if (_bmpW == Width && _bmpH == Height && _frost != null) return;
        if (_frost != null) { _frost.Dispose(); _frost = null; }
        _bmpW = Width;
        _bmpH = Height;
        try { _frost = GlassPaint.MakeFrost(Width, Height, _logo); }
        catch { _frost = null; }
    }

    private void OnKeys(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            DialogResult = DialogResult.Cancel;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            Accept();
        }
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_real.Box.Text))
        {
            _real.Box.Focus();
            Invalidate();
            return;
        }
        DialogResult = DialogResult.OK;
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            PaintCard(e.Graphics);
        }
        catch {}
    }

    private void PaintCard(Graphics g)
    {
        LayoutFields();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var card = ClientRectangle;
        GlassPaint.FillCard(g, card, _frost);

        GlassPaint.DrawDot(g, _rClose, Color.FromArgb(255, 95, 86), _hot == 0);

        DrawHeaderBrand(g);
        TextRenderer.DrawText(g, _kindTag, _fNav,
            new Rectangle(Width - Pad - 80, 0, 80, Header), Color.FromArgb(186, 186, 190),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        using (var p = new Pen(Color.FromArgb(36, 255, 255, 255)))
            g.DrawLine(p, Pad, Header, Width - Pad, Header);

        GlassPaint.DrawMark(g, _rMark, _logo);

        var copy = new Rectangle(_rMark.Right + 16, _rMark.Y, Width - Pad - _rMark.Right - 16, Mark);
        TextRenderer.DrawText(g, _heading, _fTitle,
            new Rectangle(copy.X, copy.Y + 2, copy.Width, 28), Color.FromArgb(244, 244, 246),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _sub, _fMeta,
            new Rectangle(copy.X, copy.Y + 28, copy.Width, 22), Color.FromArgb(150, 150, 154),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, "Enter to add  ·  Esc to cancel", _fMeta,
            new Rectangle(Pad, _rOk.Y, Math.Max(40, _rCancel.X - Pad - 8), BtnH),
            Color.FromArgb(120, 120, 124),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var cancelCol = _hot == 2 ? Color.White : Color.FromArgb(186, 186, 190);
        TextRenderer.DrawText(g, "Cancel", _fBtn, _rCancel, cancelCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var add = _rOk;
        if (_pressed && _hot == 1) add.Inflate(-2, -2);
        var addFill = _hot == 1
            ? Color.FromArgb(244, 244, 246)
            : Color.FromArgb(220, 220, 224);
        using (var addPath = UiShapes.RoundedRect(add, add.Height / 2))
        using (var b = new SolidBrush(addFill))
            g.FillPath(b, addPath);
        TextRenderer.DrawText(g, "Add", _fBtn, add, Color.FromArgb(18, 18, 20),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawHeaderBrand(Graphics g)
    {
        var mark = 18;
        var name = TextRenderer.MeasureText(g, "intelbyte", _fBrand, new Size(400, 40), TextFormatFlags.NoPadding);
        var total = mark + 8 + name.Width;
        var x = (Width - total) / 2;
        var y = (Header - mark) / 2;
        GlassPaint.DrawMark(g, new Rectangle(x, y, mark, mark), _logo);
        TextRenderer.DrawText(g, "intelbyte", _fBrand,
            new Rectangle(x + mark + 8, 0, name.Width + 8, Header), Color.FromArgb(210, 210, 214),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hot = Hit(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hot != -1 || _pressed)
        {
            _hot = -1;
            _pressed = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }
        var hot = Hit(e.Location);
        if (hot == 1)
        {
            _pressed = true;
            Invalidate();
            return;
        }
        if (hot == 0 || hot == 2)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }
        if (e.Y < Header) NativeMethods.DragMove(this);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed)
        {
            _pressed = false;
            if (Hit(e.Location) == 1) Accept();
            else Invalidate();
        }
        base.OnMouseUp(e);
    }

    private int Hit(Point pt)
    {
        if (_rClose.Contains(pt)) return 0;
        if (_rOk.Contains(pt)) return 1;
        if (_rCancel.Contains(pt)) return 2;
        return -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (_frost != null) { _frost.Dispose(); _frost = null; }
                if (_logo != null) _logo.Dispose();
                _fBrand.Dispose();
                _fTitle.Dispose();
                _fMeta.Dispose();
                _fBtn.Dispose();
                _fNav.Dispose();
            }
            catch {}
        }
        try { base.Dispose(disposing); } catch {}
    }
}
