using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

internal sealed class GlassView : Control
{
    public const int Frame = 0;
    public const int Pad = 28;
    public const int Header = 48;
    public const int Row = 36;
    public const int Add = 40;
    public const int Dot = 11;
    public const int DotGap = 8;
    public const int NavGap = 24;
    public const int Cluster = 12;
    public const int Logo = 72;
    public const int LeftW = 240;
    public const int ToggleW = 176;
    public const int ToggleH = 42;

    private string _big = "OFF";
    public string Big
    {
        get { return _big; }
        set
        {
            _big = value ?? "OFF";
            if (_switch != null)
            {
                _switch.On = string.Equals(_big, "ON", StringComparison.OrdinalIgnoreCase);
                _switch.Invalidate();
            }
        }
    }
    public string Sub = "Click ON to start protecting.";
    public string Status = "Off";
    public string Count = "0";
    public string Foot = "";
    private bool _startup;
    public bool Startup
    {
        get { return _startup; }
        set
        {
            _startup = value;
            if (_startSwitch != null) _startSwitch.On = value;
        }
    }
    private bool _streamProof;
    public bool StreamProof
    {
        get { return _streamProof; }
        set
        {
            _streamProof = value;
            if (_proofSwitch != null) _proofSwitch.On = value;
        }
    }
    public bool Busy;
    public bool Loading;
    public string LoadText = "Turning on";
    public BareField Field { get { return _search.Box; } }

    public event EventHandler CloseClick;
    public event EventHandler MinimizeClick;
    public event EventHandler ToggleClick;
    public event EventHandler AddClick;
    public event Action<string> KindClick;
    public event EventHandler RemoveClick;
    public event EventHandler StartupClick;
    public event EventHandler StreamProofClick;

    private readonly Image _logo;
    private readonly GlassField _search;
    private readonly OnOffSwitch _switch;
    private readonly OnOffSwitch _proofSwitch;
    private readonly OnOffSwitch _startSwitch;
    private Bitmap _frost;
    private int _bmpW, _bmpH;
    private int _hot = -1;
    private int _listHover = -1;
    private int _listSel = -1;
    private readonly List<RowData> _rows = new List<RowData>();
    private readonly HashSet<string> _revealed = new HashSet<string>();

    private Rectangle _card;
    private Rectangle _rClose, _rMin, _rGreen, _rLogo, _rOff, _rOn, _rBar, _rAdd, _rField, _rList, _rStart, _rProof, _rFoot, _rSite, _rDisc;
    private Rectangle[] _nav = new Rectangle[3];
    private readonly string[] _navText = { "EMAIL", "PHONE", "CUSTOM" };

    private readonly Font _fTitle;
    private readonly Font _fBrand;
    private readonly Font _fNav;
    private readonly Font _fToggle;
    private readonly Font _fList;
    private readonly Font _fMeta;
    private System.Windows.Forms.Timer _spin;
    private float _spinAng;

    public GlassView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.FromArgb(16, 16, 20);
        Font = new Font("Segoe UI", 9f);
        _logo = Program.LoadLogo();
        _fTitle = Program.LoadDetectiveFont(11f);
        _fBrand = Program.LoadDetectiveFont(18f);
        _fNav = new Font("Segoe UI Semibold", 8.5f);
        _fToggle = new Font("Segoe UI Semibold", 10f);
        _fList = new Font("Segoe UI", 12f);
        _fMeta = new Font("Segoe UI", 9f);

        _search = new GlassField("Hide another value") { ShowPlus = true };
        Controls.Add(_search);
        _search.PlusClick += delegate { if (AddClick != null && !Busy) AddClick(this, EventArgs.Empty); };
        _search.Box.GotFocus += delegate { Invalidate(); };
        _search.Box.LostFocus += delegate { Invalidate(); };
        _search.Box.TextChanged += delegate { Invalidate(); };
        _search.Visible = false;
        _switch = new OnOffSwitch();
        _switch.Toggled += delegate { if (ToggleClick != null) ToggleClick(this, EventArgs.Empty); };
        Controls.Add(_switch);
        _switch.BringToFront();
        _proofSwitch = new OnOffSwitch(96, 30);
        _proofSwitch.AccessibleName = "Stream proof on or off";
        _proofSwitch.Toggled += delegate { if (StreamProofClick != null) StreamProofClick(this, EventArgs.Empty); };
        Controls.Add(_proofSwitch);
        _proofSwitch.BringToFront();
        _startSwitch = new OnOffSwitch(96, 30);
        _startSwitch.AccessibleName = "Auto-start on or off";
        _startSwitch.Toggled += delegate { if (StartupClick != null) StartupClick(this, EventArgs.Empty); };
        Controls.Add(_startSwitch);
        _startSwitch.BringToFront();
        SetLoading(true, "Turning on");
    }

    public void SetLoading(bool on, string text)
    {
        Loading = on;
        if (!string.IsNullOrEmpty(text)) LoadText = text;
        _search.Visible = !on;
        if (on)
        {
            if (_spin == null)
            {
                _spin = new System.Windows.Forms.Timer { Interval = 30 };
                _spin.Tick += delegate
                {
                    _spinAng += 8f;
                    if (_spinAng >= 360f) _spinAng -= 360f;
                    Invalidate();
                };
            }
            _spin.Start();
        }
        else
        {
            if (_spin != null) _spin.Stop();
            if (_frost == null) RebuildAtmosphere();
        }
        if (_switch != null) _switch.Visible = !on;
        if (_proofSwitch != null) _proofSwitch.Visible = !on;
        if (_startSwitch != null) _startSwitch.Visible = !on;
        Invalidate();
    }

    public RowData SelectedRow
    {
        get
        {
            if (_listSel < 0 || _listSel >= _rows.Count) return null;
            return _rows[_listSel];
        }
    }

    public void Forget(string real)
    {
        if (real != null) _revealed.Remove(real);
    }

    public void SetRows(IList<RowData> rows)
    {
        var keep = SelectedRow != null ? SelectedRow.Real : null;
        _rows.Clear();
        if (rows != null) foreach (var r in rows) _rows.Add(r);
        _listSel = -1;
        if (keep != null)
        {
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].Real == keep) { _listSel = i; break; }
        }
        Invalidate();
    }

    public string FieldText
    {
        get { return Field.Text; }
        set { Field.Text = value; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutRects();
        _search.Bounds = _rBar;
        _search.Busy = Busy;
        _search.Visible = !Loading;
        if (_proofSwitch != null) _proofSwitch.Visible = !Loading;
        if (_startSwitch != null) _startSwitch.Visible = !Loading;
        if (!Loading && (_bmpW != Width || _bmpH != Height))
            RebuildAtmosphere();
        Invalidate();
    }

    private void LayoutRects()
    {
        _card = new Rectangle(0, 0, Width, Height);
        if (_card.Width < 200 || _card.Height < 200) return;

        var yDot = _card.Y + (Header - Dot) / 2;
        _rClose = new Rectangle(_card.X + Pad, yDot, Dot, Dot);
        _rMin = new Rectangle(_rClose.X + Dot + DotGap, yDot, Dot, Dot);
        _rGreen = new Rectangle(_rMin.X + Dot + DotGap, yDot, Dot, Dot);

        var slot = 0;
        for (var i = 0; i < _navText.Length; i++)
            slot = Math.Max(slot, TextRenderer.MeasureText(_navText[i], _fNav).Width);
        var navRight = _card.Right - Pad;
        for (var i = _navText.Length - 1; i >= 0; i--)
        {
            _nav[i] = new Rectangle(navRight - slot, _card.Y, slot, Header);
            navRight -= slot + NavGap;
        }

        var top = _card.Y + Header + Pad;
        var left = _card.X + Pad;
        var right = left + LeftW + Pad;
        var rightW = _card.Right - Pad - right;
        var bottom = _card.Bottom - Pad;

        _rLogo = new Rectangle(left, top, Logo, Logo);
        var linkY = top + Logo + 80;
        _rSite = new Rectangle(left, linkY, 88, 18);
        _rDisc = new Rectangle(left, linkY + 18, 150, 18);
        _rOff = new Rectangle(left, linkY + 44, ToggleW / 2, ToggleH);
        _rOn = new Rectangle(left + ToggleW / 2, _rOff.Y, ToggleW / 2, ToggleH);
        if (_switch != null)
        {
            _switch.Bounds = new Rectangle(left, linkY + 44, ToggleW, ToggleH);
            _switch.On = string.Equals(Big, "ON", StringComparison.OrdinalIgnoreCase);
            _switch.Visible = !Loading;
            _switch.BringToFront();
        }

        _rBar = new Rectangle(right, top, rightW, Add);
        _rAdd = new Rectangle(_rBar.Right - Add, _rBar.Y, Add, Add);
        _rField = new Rectangle(_rBar.X, _rBar.Y, _rBar.Width - Add, Add);
        _rList = new Rectangle(right, top + Add + Pad, rightW, bottom - Row - (top + Add + Pad));
        const int settingW = 96;
        const int settingGap = 12;
        var settingY = bottom - 3 - 30;
        _rStart = new Rectangle(right, bottom - Row, Math.Max(40, rightW - settingW - settingGap), Row);
        _rProof = new Rectangle(left, bottom - Row, Math.Max(40, LeftW - settingW - settingGap), Row);
        _rFoot = new Rectangle(left, _rProof.Y - 18, LeftW, 18);
        if (_proofSwitch != null)
        {
            _proofSwitch.Bounds = new Rectangle(left + LeftW - settingW, settingY, settingW, 30);
            _proofSwitch.On = StreamProof;
            _proofSwitch.Visible = !Loading;
            _proofSwitch.BringToFront();
        }
        if (_startSwitch != null)
        {
            _startSwitch.Bounds = new Rectangle(right + rightW - settingW, settingY, settingW, 30);
            _startSwitch.On = Startup;
            _startSwitch.Visible = !Loading;
            _startSwitch.BringToFront();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            PaintCard(e.Graphics);
        }
        catch {}
    }

    private void PaintCard(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_rOff.Width < 8) LayoutRects();
        if (_card.Width < 8 || _card.Height < 8) return;

        GlassPaint.FillCard(g, _card, Loading ? null : _frost);

        GlassPaint.DrawDot(g, _rClose, Color.FromArgb(255, 95, 86), _hot == 0);
        GlassPaint.DrawDot(g, _rMin, Color.FromArgb(255, 189, 46), _hot == 1);
        GlassPaint.DrawDot(g, _rGreen, Color.FromArgb(39, 201, 63), _hot == 2);

        if (Loading)
        {
            DrawLoading(g);
            return;
        }

        DrawHeaderBrand(g);

        for (var i = 0; i < _navText.Length; i++)
        {
            var hot = _hot == 10 + i;
            var col = hot ? Color.White : Color.FromArgb(190, 190, 194);
            TextRenderer.DrawText(g, _navText[i], _fNav, _nav[i], col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (hot)
            {
                var mid = _nav[i].X + _nav[i].Width / 2;
                using (var p = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f))
                    g.DrawLine(p, mid - 10, _card.Y + Header - 8, mid + 10, _card.Y + Header - 8);
            }
        }

        using (var p = new Pen(Color.FromArgb(36, 255, 255, 255)))
            g.DrawLine(p, _card.X + Pad, _card.Y + Header, _card.Right - Pad, _card.Y + Header);

        GlassPaint.DrawMark(g, _rLogo, _logo);

        var brandY = _rLogo.Bottom + 14;
        TextRenderer.DrawText(g, "IntelByte", _fBrand,
            new Rectangle(_rLogo.X, brandY, LeftW, 26), Color.FromArgb(236, 236, 238),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Screen privacy", _fMeta,
            new Rectangle(_rLogo.X, brandY + 26, LeftW, 18), Color.FromArgb(150, 150, 154),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "v" + Program.AppVersion, _fMeta,
            new Rectangle(_rLogo.X, brandY + 44, LeftW, 16), Color.FromArgb(125, 125, 130),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        var siteCol = _hot == 7 ? Color.White : Color.FromArgb(170, 170, 174);
        var discCol = _hot == 8 ? Color.White : Color.FromArgb(170, 170, 174);
        TextRenderer.DrawText(g, "intelbyte.cc", _fMeta, _rSite, siteCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "discord.gg/intelbyte", _fMeta, _rDisc, discCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(g, Sub, _fMeta,
            new Rectangle(_rOff.X, _rOff.Bottom + Cluster, LeftW, 40), Color.FromArgb(160, 160, 164),
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        _search.Busy = Busy;
        _search.Bounds = _rBar;

        PaintList(g);

        var foot = !string.IsNullOrEmpty(Foot)
            ? Foot
            : (Status + "  ·  " + Count + " hidden");
        TextRenderer.DrawText(g, foot, _fMeta, _rFoot, Color.FromArgb(168, 168, 172),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var startCol = _hot == 4 ? Color.White : Color.FromArgb(205, 205, 210);
        TextRenderer.DrawText(g, "Auto-start when PC turns on", _fMeta, _rStart, startCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        var proofCol = _hot == 9 ? Color.White : Color.FromArgb(205, 205, 210);
        TextRenderer.DrawText(g, "Stream proof", _fMeta, _rProof, proofCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void DrawLoading(Graphics g)
    {
        var side = 72;
        var cx = _card.X + _card.Width / 2;
        var cy = _card.Y + _card.Height / 2 - 24;
        var mark = new Rectangle(cx - side / 2, cy - side / 2, side, side);
        GlassPaint.DrawMark(g, mark, _logo);

        var ring = mark;
        ring.Inflate(14, 14);
        using (var dim = new Pen(Color.FromArgb(40, 255, 255, 255), 1.6f))
            g.DrawEllipse(dim, ring);
        using (var hi = new Pen(Color.FromArgb(230, 236, 236, 238), 1.8f))
        {
            hi.StartCap = LineCap.Round;
            hi.EndCap = LineCap.Round;
            g.DrawArc(hi, ring, _spinAng, 78);
        }

        var msg = string.IsNullOrEmpty(LoadText) ? "Turning on" : LoadText;
        TextRenderer.DrawText(g, "IntelByte", _fBrand,
            new Rectangle(_card.X, mark.Bottom + 22, _card.Width, 28), Color.FromArgb(236, 236, 238),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, msg, _fMeta,
            new Rectangle(_card.X, mark.Bottom + 50, _card.Width, 22), Color.FromArgb(150, 150, 154),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawHeaderBrand(Graphics g)
    {
        var mark = 18;
        var name = TextRenderer.MeasureText(g, "intelbyte", _fTitle, new Size(400, 40), TextFormatFlags.NoPadding);
        var total = mark + 8 + name.Width;
        var x = _card.X + (_card.Width - total) / 2;
        var y = _card.Y + (Header - mark) / 2;
        GlassPaint.DrawMark(g, new Rectangle(x, y, mark, mark), _logo);
        TextRenderer.DrawText(g, "intelbyte", _fTitle,
            new Rectangle(x + mark + 8, _card.Y, name.Width + 8, Header), Color.FromArgb(210, 210, 214),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void PaintList(Graphics g)
    {
        if (_rows.Count == 0)
        {
            TextRenderer.DrawText(g, "Nothing hidden yet", _fList, _rList,
                Color.FromArgb(200, 200, 204), TextFormatFlags.Left | TextFormatFlags.Top);
            return;
        }
        var vis = Math.Min(_rows.Count, Math.Max(0, _rList.Height / Row));
        for (var i = 0; i < vis; i++)
        {
            var rc = new Rectangle(_rList.X, _rList.Y + i * Row, _rList.Width, Row);
            if (i == _listHover || i == _listSel)
            {
                var pill = rc;
                pill.Inflate(0, -2);
                using (var path = UiShapes.RoundedRect(pill, 8))
                using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
                    g.FillPath(b, path);
            }
            var row = _rows[i];
            var label = _revealed.Contains(row.Real) ? row.Real : row.Fake;
            TextRenderer.DrawText(g, label, _fList, new Rectangle(rc.X, rc.Y, rc.Width - 28, rc.Height),
                Color.FromArgb(230, 230, 232),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (i == _listHover)
                TextRenderer.DrawText(g, "×", Font, new Rectangle(rc.Right - 24, rc.Y, 24, rc.Height),
                    Color.FromArgb(186, 186, 190),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hot = Hit(e.Location);
        var list = ListAt(e.Location);
        if (hot != _hot || list != _listHover)
        {
            _hot = hot;
            _listHover = list;
            Cursor = (hot >= 0 || list >= 0) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hot != -1 || _listHover != -1)
        {
            _hot = -1;
            _listHover = -1;
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
        if (hot == 0) { if (CloseClick != null) CloseClick(this, EventArgs.Empty); return; }
        if (hot == 1) { if (MinimizeClick != null) MinimizeClick(this, EventArgs.Empty); return; }
        if (hot == 2) return;
        if (Loading)
        {
            if (_card.Contains(e.Location)) NativeMethods.DragMove(FindForm());
            return;
        }
        if (hot == 7) { OpenUrl("https://intelbyte.cc"); return; }
        if (hot == 8) { OpenUrl("https://discord.gg/intelbyte"); return; }
        if (hot == 5 || hot == 6 || InToggleRail(e.Location))
        {
            if (ToggleClick != null) ToggleClick(this, EventArgs.Empty);
            return;
        }
        if (hot >= 10 && hot <= 12)
        {
            if (KindClick != null) KindClick(_navText[hot - 10].ToLowerInvariant());
            return;
        }
        var li = ListAt(e.Location);
        if (li >= 0)
        {
            _listSel = li;
            var row = _rows[li];
            var rc = new Rectangle(_rList.X, _rList.Y + li * Row, _rList.Width, Row);
            if (e.X > rc.Right - 28)
            {
                if (RemoveClick != null) RemoveClick(this, EventArgs.Empty);
            }
            else
            {
                if (_revealed.Contains(row.Real)) _revealed.Remove(row.Real);
                else _revealed.Add(row.Real);
            }
            Invalidate();
            return;
        }
        if (_card.Contains(e.Location) && !_rBar.Contains(e.Location) && !InToggleRail(e.Location))
            NativeMethods.DragMove(FindForm());
    }

    private bool InToggleRail(Point pt)
    {
        return _switch != null && _switch.Visible && _switch.Bounds.Contains(pt);
    }

    private int Hit(Point pt)
    {
        if (_rClose.Contains(pt)) return 0;
        if (_rMin.Contains(pt)) return 1;
        if (_rGreen.Contains(pt)) return 2;
        if (_rSite.Contains(pt)) return 7;
        if (_rDisc.Contains(pt)) return 8;
        for (var i = 0; i < _nav.Length; i++)
            if (_nav[i].Contains(pt)) return 10 + i;
        if (InToggleRail(pt)) return 6;
        return -1;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch {}
    }

    private int ListAt(Point pt)
    {
        if (!_rList.Contains(pt) || _rows.Count == 0) return -1;
        var i = (pt.Y - _rList.Y) / Row;
        if (i < 0 || i >= _rows.Count) return -1;
        if (i >= _rList.Height / Row) return -1;
        return i;
    }

    private void RebuildAtmosphere()
    {
        if (_frost != null) { _frost.Dispose(); _frost = null; }
        _bmpW = Width;
        _bmpH = Height;
        if (Width < 8 || Height < 8) return;
        try { _frost = GlassPaint.MakeFrost(Width, Height, _logo); }
        catch { _frost = null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_frost != null) { _frost.Dispose(); _frost = null; }
            if (_spin != null) { _spin.Stop(); _spin.Dispose(); _spin = null; }
            if (_logo != null) _logo.Dispose();
            _fTitle.Dispose();
            _fBrand.Dispose();
            _fNav.Dispose();
            _fToggle.Dispose();
            _fList.Dispose();
            _fMeta.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class OnOffSwitch : Control
{
    private bool _on;
    public bool On
    {
        get { return _on; }
        set
        {
            if (_on == value) return;
            _on = value;
            Invalidate();
        }
    }
    public event EventHandler Toggled;
    private readonly Font _font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
    private bool _hover;
    private bool _pressed;

    public OnOffSwitch() : this(GlassView.ToggleW, GlassView.ToggleH) {}

    public OnOffSwitch(int width, int height)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "IntelByte protection on or off";
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = Color.FromArgb(28, 28, 32);
        Size = new Size(width, height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var activate = _pressed && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            if (activate && Toggled != null) Toggled(this, EventArgs.Empty);
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Toggled != null)
        {
            e.Handled = true;
            Toggled(this, EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 8 || Height < 8) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var pill = new Rectangle(1, 1, Width - 3, Height - 3);
        var radius = Math.Max(8, Math.Min(pill.Width, pill.Height) / 2);
        var trackColor = On
            ? (_hover ? Color.FromArgb(37, 84, 64) : Color.FromArgb(31, 67, 53))
            : (_hover ? Color.FromArgb(48, 50, 60) : Color.FromArgb(36, 38, 46));

        var shadow = pill;
        shadow.Offset(0, 2);
        using (var shadowPath = UiShapes.RoundedRect(shadow, radius))
        using (var shadowFill = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            g.FillPath(shadowFill, shadowPath);
        using (var track = UiShapes.RoundedRect(pill, radius))
        using (var trackFill = new SolidBrush(trackColor))
            g.FillPath(trackFill, track);
        var half = Width / 2;
        var thumb = On
            ? new Rectangle(half + 2, 3, Width - half - 6, Height - 8)
            : new Rectangle(3, 3, half - 4, Height - 8);
        if (_pressed) thumb.Offset(On ? -1 : 1, 0);
        using (var tpath = UiShapes.RoundedRect(thumb, Math.Max(6, (Height - 8) / 2)))
        using (var fill = new SolidBrush(On
            ? (_hover ? Color.FromArgb(112, 235, 166) : Color.FromArgb(92, 218, 148))
            : (_hover ? Color.FromArgb(250, 250, 252) : Color.FromArgb(232, 233, 238))))
            g.FillPath(fill, tpath);
        using (var thumbEdge = new Pen(On ? Color.FromArgb(115, 193, 158) : Color.FromArgb(190, 190, 198), 1f))
        using (var thumbOutline = UiShapes.RoundedRect(thumb, Math.Max(6, (Height - 8) / 2)))
            g.DrawPath(thumbEdge, thumbOutline);
        using (var ring = new Pen(On ? Color.FromArgb(180, 112, 235, 166) : Color.FromArgb(150, 210, 212, 220)))
        using (var outline = UiShapes.RoundedRect(pill, radius))
            g.DrawPath(ring, outline);
        var dot = new Rectangle(On ? Width - 22 : 11, Height / 2 - 3, 6, 6);
        using (var dotFill = new SolidBrush(On ? Color.FromArgb(20, 92, 61) : Color.FromArgb(126, 128, 138)))
            g.FillEllipse(dotFill, dot);
        var offCol = !On ? Color.FromArgb(25, 27, 32) : Color.FromArgb(180, 205, 194);
        var onCol = On ? Color.FromArgb(18, 48, 34) : Color.FromArgb(170, 172, 182);
        TextRenderer.DrawText(g, "OFF", _font, new Rectangle(0, 0, half, Height), offCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "ON", _font, new Rectangle(half, 0, Width - half, Height), onCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused)
        {
            var focus = pill;
            focus.Inflate(-2, -2);
            using (var focusPen = new Pen(Color.FromArgb(190, 255, 255, 255), 1f))
            using (var focusPath = UiShapes.RoundedRect(focus, Math.Min(focus.Width, focus.Height) / 2))
                g.DrawPath(focusPen, focusPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) try { _font.Dispose(); } catch {}
        base.Dispose(disposing);
    }
}

internal sealed class RowData
{
    public string Type;
    public string Real;
    public string Fake;
}
