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
    public const int ToggleW = 152;
    public const int ToggleH = 30;

    public string Big = "OFF";
    public string Sub = "Click ON to start protecting.";
    public string Status = "Off";
    public string Count = "0";
    public string Foot = "";
    public bool Startup;
    public bool StreamProof;
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
        var linkY = top + Logo + 62;
        _rSite = new Rectangle(left, linkY, 88, 18);
        _rDisc = new Rectangle(left, linkY + 18, 150, 18);
        _rOff = new Rectangle(left, linkY + 44, ToggleW / 2, ToggleH);
        _rOn = new Rectangle(left + ToggleW / 2, _rOff.Y, ToggleW / 2, ToggleH);

        _rBar = new Rectangle(right, top, rightW, Add);
        _rAdd = new Rectangle(_rBar.Right - Add, _rBar.Y, Add, Add);
        _rField = new Rectangle(_rBar.X, _rBar.Y, _rBar.Width - Add, Add);
        _rList = new Rectangle(right, top + Add + Pad, rightW, bottom - Row - (top + Add + Pad));
        _rStart = new Rectangle(right, bottom - Row, rightW, Row);
        _rProof = new Rectangle(left, bottom - Row, LeftW, Row);
        _rFoot = new Rectangle(left, _rProof.Y - 18, LeftW, 18);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
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

        var siteCol = _hot == 7 ? Color.White : Color.FromArgb(170, 170, 174);
        var discCol = _hot == 8 ? Color.White : Color.FromArgb(170, 170, 174);
        TextRenderer.DrawText(g, "intelbyte.cc", _fMeta, _rSite, siteCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "discord.gg/intelbyte", _fMeta, _rDisc, discCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        DrawToggle(g);

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

        var startCol = _hot == 4 ? Color.White : Color.FromArgb(186, 186, 190);
        TextRenderer.DrawText(g, Startup ? "Auto-start when PC turns on  ·  On" : "Auto-start when PC turns on  ·  Off",
            _fMeta, _rStart, startCol,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        var proofCol = _hot == 9 ? Color.White : Color.FromArgb(186, 186, 190);
        TextRenderer.DrawText(g, StreamProof ? "Stream proof  ·  On" : "Stream proof  ·  Off",
            _fMeta, _rProof, proofCol,
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

    private void DrawToggle(Graphics g)
    {
        var on = string.Equals(Big, "ON", StringComparison.OrdinalIgnoreCase);
        var pill = new Rectangle(_rOff.X, _rOff.Y, ToggleW, ToggleH);
        if (pill.Width < 8) return;
        using (var track = UiShapes.RoundedRect(pill, ToggleH / 2))
        using (var trackFill = new SolidBrush(Color.FromArgb(230, 28, 28, 32)))
            g.FillPath(trackFill, track);

        var thumb = on ? _rOn : _rOff;
        thumb.Inflate(-3, -3);
        using (var tpath = UiShapes.RoundedRect(thumb, Math.Max(4, (ToggleH - 6) / 2)))
        using (var fill = new SolidBrush(on
            ? Color.FromArgb(186, 214, 176)
            : Color.FromArgb(236, 236, 240)))
            g.FillPath(fill, tpath);

        using (var ring = new Pen(Color.FromArgb(90, 255, 255, 255)))
        using (var outline = UiShapes.RoundedRect(pill, ToggleH / 2))
            g.DrawPath(ring, outline);

        var offCol = !on ? Color.FromArgb(20, 20, 22) : Color.FromArgb(210, 210, 214);
        var onCol = on ? Color.FromArgb(20, 20, 22) : Color.FromArgb(210, 210, 214);
        TextRenderer.DrawText(g, "OFF", _fToggle, _rOff, offCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "ON", _fToggle, _rOn, onCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
        if (hot == 4) { if (StartupClick != null) StartupClick(this, EventArgs.Empty); return; }
        if (hot == 9) { if (StreamProofClick != null) StreamProofClick(this, EventArgs.Empty); return; }
        if (hot == 7) { OpenUrl("https://intelbyte.cc"); return; }
        if (hot == 8) { OpenUrl("https://discord.gg/intelbyte"); return; }
        if (hot == 5 || hot == 6)
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
        if (_card.Contains(e.Location) && !_rBar.Contains(e.Location))
            NativeMethods.DragMove(FindForm());
    }

    private int Hit(Point pt)
    {
        if (_rClose.Contains(pt)) return 0;
        if (_rMin.Contains(pt)) return 1;
        if (_rGreen.Contains(pt)) return 2;
        if (_rOff.Contains(pt)) return 5;
        if (_rOn.Contains(pt)) return 6;
        if (_rStart.Contains(pt)) return 4;
        if (_rProof.Contains(pt)) return 9;
        if (_rSite.Contains(pt)) return 7;
        if (_rDisc.Contains(pt)) return 8;
        for (var i = 0; i < _nav.Length; i++)
            if (_nav[i].Contains(pt)) return 10 + i;
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
        _frost = GlassPaint.MakeFrost(Width, Height, _logo);
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

internal sealed class RowData
{
    public string Type;
    public string Real;
    public string Fake;
}
