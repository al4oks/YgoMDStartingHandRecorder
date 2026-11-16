using Ygo.CardLib;

namespace Ygo.Labeler;

public sealed class MainForm : Form, IMessageFilter
{
    private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp" };

    private readonly HandConfig _cfg;
    private readonly CardDatabase _db;
    private readonly LabelCodec _codec;
    private readonly LabelStore _store;

    private string _dir = "";
    private List<string> _all = new();
    private List<string> _view = new();
    private bool _suppress;

    private Image? _orig;
    private double _factor = 1;
    private bool _dragging;
    private Point _dragCursor;
    private Point _dragScroll;

    private readonly TextBox _dirBox = new() { ReadOnly = true, Width = 470, Location = new Point(6, 9) };
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Location = new Point(566, 8) };
    private readonly Label _progress = new() { AutoSize = true, Location = new Point(688, 13) };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Panel _picHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.AppWorkspace };
    private readonly PictureBox _pic = new() { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 0) };
    private readonly Label _zoomLabel = new() { AutoSize = true, Location = new Point(330, 10) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox[] _slots;
    private readonly ListBox _popup = new() { Visible = false, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private IReadOnlyList<Card> _popupHits = Array.Empty<Card>();
    private int _activeSlot = -1;

    public MainForm(string root, HandConfig cfg, CardDatabase db, string? initialDir)
    {
        _cfg = cfg;
        _db = db;
        _codec = new LabelCodec(cfg);
        _store = new LabelStore(_codec);
        _slots = Enumerable.Range(0, cfg.Slots).Select(_ => new TextBox { Width = 205, Margin = new Padding(3, 8, 3, 0) }).ToArray();

        Text = "Ygo.Labeler";
        MinimumSize = new Size(1100, 700);
        Size = new Size(1360, 880);
        KeyPreview = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 42 };
        var browse = new Button { Text = "浏览…", Location = new Point(482, 7), Width = 76 };
        browse.Click += (_, _) => Browse();
        _filter.Items.AddRange(new object[] { "全部", "未标注", "已标注", "跳过" });
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => RefreshList(null);
        top.Controls.AddRange(new Control[] { _dirBox, browse, _filter, _progress });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 122 };
        var slotPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, WrapContents = false, Padding = new Padding(6, 0, 0, 0) };
        for (var i = 0; i < _slots.Length; i++)
        {
            slotPanel.Controls.Add(new Label { Text = $"{i + 1}", AutoSize = true, Margin = new Padding(8, 12, 0, 0) });
            slotPanel.Controls.Add(_slots[i]);
            WireSlot(i, _slots[i]);
        }
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(6, 4, 0, 0) };
        var save = new Button { Text = "保存并下一张 (Enter)", AutoSize = true };
        var skip = new Button { Text = "跳过 (S)", AutoSize = true };
        var prev = new Button { Text = "← 上一张", AutoSize = true };
        var next = new Button { Text = "下一张 →", AutoSize = true };
        var paste = new Button { Text = "粘贴拆填 (Ctrl+V)", AutoSize = true };
        save.Click += (_, _) => SaveAndNext();
        skip.Click += (_, _) => SkipCurrent();
        prev.Click += (_, _) => Step(-1);
        next.Click += (_, _) => Step(1);
        paste.Click += (_, _) => PasteSplit();
        btnPanel.Controls.AddRange(new Control[] { save, skip, prev, next, paste });
        bottom.Controls.Add(_status);
        bottom.Controls.Add(btnPanel);
        bottom.Controls.Add(slotPanel);

        var left = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(6) };
        _list.SelectedIndexChanged += (_, _) => { HidePopup(); LoadCurrent(); };
        left.Controls.Add(_list);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 36 };
        var fit = new Button { Text = "适应", Location = new Point(6, 5), Width = 60 };
        var one = new Button { Text = "1:1", Location = new Point(72, 5), Width = 60 };
        var hand = new Button { Text = "手牌区放大", Location = new Point(138, 5), Width = 100 };
        fit.Click += (_, _) => { SetImage(_orig); _factor = 1; ApplyZoom(); };
        one.Click += (_, _) => OneToOne();
        hand.Click += (_, _) => HandZoom();
        toolbar.Controls.AddRange(new Control[] { fit, one, hand, _zoomLabel });
        _picHost.Controls.Add(_pic);
        _pic.MouseDown += PicMouseDown;
        _pic.MouseMove += PicMouseMove;
        _pic.MouseUp += (_, _) => _dragging = false;
        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(_picHost);
        center.Controls.Add(toolbar);

        Controls.Add(center);
        Controls.Add(left);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_popup);
        _popup.MouseClick += (_, e) =>
        {
            var idx = _popup.IndexFromPoint(e.Location);
            if (idx >= 0) Commit(idx);
        };
        _popup.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(_popup.SelectedIndex); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { HidePopup(); if (_activeSlot >= 0) _slots[_activeSlot].Focus(); e.Handled = true; }
        };

        _dir = initialDir != null && Directory.Exists(initialDir)
            ? initialDir
            : Directory.Exists(Path.Combine(root, "samples", "own"))
                ? Path.Combine(root, "samples", "own")
                : Path.Combine(root, "samples");
        _dirBox.Text = _dir;
        RefreshList(null);
        Application.AddMessageFilter(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        _orig?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (ActiveControl == _list || ActiveControl == _pic || ActiveControl == _picHost || ActiveControl == null)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: Step(-1); e.Handled = true; break;
                case Keys.Right: Step(1); e.Handled = true; break;
                case Keys.S: SkipCurrent(); e.Handled = true; break;
                case Keys.Enter: SaveAndNext(); e.Handled = true; break;
            }
        }
        base.OnKeyDown(e);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != 0x020A) return false; // WM_MOUSEWHEEL
        var cursor = Cursor.Position;
        if (_popup.Visible && _popup.RectangleToScreen(_popup.ClientRectangle).Contains(cursor))
        {
            var d = (short)((long)m.WParam >> 16);
            _popup.TopIndex = Math.Clamp(_popup.TopIndex - Math.Sign(d) * 3, 0, Math.Max(0, _popup.Items.Count - 1));
            return true;
        }
        if (!_picHost.ClientRectangle.Contains(_picHost.PointToClient(cursor))) return false;
        var delta = (short)((long)m.WParam >> 16);
        SetFactor(_factor * (delta > 0 ? 1.15 : 1 / 1.15));
        return true;
    }

    private void WireSlot(int i, TextBox tb)
    {
        tb.Enter += (_, _) => _activeSlot = i;
        tb.Leave += (_, _) =>
        {
            if (!_popup.RectangleToScreen(_popup.ClientRectangle).Contains(Cursor.Position)) HidePopup();
        };
        tb.TextChanged += (_, _) =>
        {
            if (_suppress) return;
            _activeSlot = i;
            var t = tb.Text;
            if (t.Trim().Length == 0) { HidePopup(); return; }
            var hits = _db.Search(t, 20);
            _popupHits = hits;
            if (hits.Count == 0) { HidePopup(); return; }
            _suppress = true;
            _popup.BeginUpdate();
            _popup.Items.Clear();
            foreach (var h in hits) _popup.Items.Add(DisplayFor(h, hits));
            _popup.EndUpdate();
            _suppress = false;
            _popup.SelectedIndex = -1;
            ShowPopupUnder(tb);
        };
        tb.KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    if (_popup.Visible)
                    {
                        _popup.SelectedIndex = Math.Min(_popup.SelectedIndex + 1, _popup.Items.Count - 1);
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                    break;
                case Keys.Up:
                    if (_popup.Visible)
                    {
                        _popup.SelectedIndex = Math.Max(_popup.SelectedIndex - 1, 0);
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                    break;
                case Keys.Enter:
                    if (_popup.Visible && _popup.SelectedIndex >= 0) Commit(_popup.SelectedIndex);
                    else SaveAndNext();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    if (_popup.Visible) HidePopup();
                    else SetSlotText(i, "");
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Tab:
                    HidePopup();
                    break;
                case Keys.V when e.Control:
                    var clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                    if (clip.Contains(_cfg.NameSeparator) && clip.Split(_cfg.NameSeparator).Length == _cfg.Slots)
                    {
                        PasteSplit();
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                    break;
            }
        };
    }

    // Card password only shown when several cards share the same display name.
    private static string DisplayFor(Card card, IReadOnlyList<Card> hits)
    {
        var name = card.DisplayName;
        return hits.Count(h => h.DisplayName == name) > 1 ? $"{name}  [{card.Id}]" : name;
    }

    private void ShowPopupUnder(TextBox tb)
    {
        var screen = tb.RectangleToScreen(tb.ClientRectangle);
        var top = PointToClient(new Point(screen.X, screen.Y));
        var itemH = _popup.IsHandleCreated ? _popup.ItemHeight : 18;
        var h = Math.Min(_popupHits.Count * itemH + 4, 320);
        // slots sit at the form bottom: open upward when there is no room below
        var y = top.Y + screen.Height + 2;
        if (y + h > ClientSize.Height) y = Math.Max(0, top.Y - h - 2);
        _popup.Location = new Point(top.X, y);
        _popup.Width = tb.Width;
        _popup.Height = h;
        _popup.Visible = true;
        _popup.BringToFront();
    }

    private void HidePopup()
    {
        _popup.Visible = false;
        _popupHits = Array.Empty<Card>();
    }

    private void Commit(int idx)
    {
        if (idx < 0 || idx >= _popupHits.Count || _activeSlot < 0) return;
        var slot = _activeSlot;
        SetSlotText(slot, _popupHits[idx].DisplayName);
        HidePopup();
        _slots[slot].Focus();
    }

    private void SetSlotText(int i, string text)
    {
        _suppress = true;
        _slots[i].Text = text;
        _suppress = false;
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = Directory.Exists(_dir) ? _dir : "" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _dir = dlg.SelectedPath;
        _dirBox.Text = _dir;
        RefreshList(null);
    }

    private void RefreshList(string? selectName)
    {
        _all = Directory.EnumerateFiles(_dir)
            .Where(p => Exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Select(p => Path.GetFileName(p)!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        int labeled = 0, skipped = 0;
        foreach (var n in _all)
        {
            var stem = Path.GetFileNameWithoutExtension(n);
            if (_codec.IsSkipped(stem)) skipped++;
            else if (_codec.TryParse(stem, out _, out _)) labeled++;
        }
        _view = _filter.SelectedIndex switch
        {
            1 => _all.Where(n => !_codec.IsSkipped(Path.GetFileNameWithoutExtension(n)) && !_codec.TryParse(Path.GetFileNameWithoutExtension(n), out _, out _)).ToList(),
            2 => _all.Where(n => _codec.TryParse(Path.GetFileNameWithoutExtension(n), out _, out _)).ToList(),
            3 => _all.Where(n => _codec.IsSkipped(Path.GetFileNameWithoutExtension(n))).ToList(),
            _ => _all.ToList(),
        };
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var n in _view) _list.Items.Add(n);
        _list.EndUpdate();
        if (selectName != null && _view.Contains(selectName)) _list.SelectedItem = selectName;
        else if (_view.Count > 0) _list.SelectedIndex = 0;
        else LoadCurrent();
        _progress.Text = $"{_view.Count}/{_all.Count}  已标={labeled} 跳过={skipped} 未标={_all.Count - labeled - skipped}";
        Text = $"Ygo.Labeler — {_dir}";
    }

    private void LoadCurrent()
    {
        HidePopup();
        var name = _list.SelectedItem as string;
        if (name == null)
        {
            _orig?.Dispose();
            _orig = null;
            SetImage(null);
            for (var i = 0; i < _slots.Length; i++) SetSlotText(i, "");
            _status.Text = "";
            return;
        }
        using (var ms = new MemoryStream(File.ReadAllBytes(Path.Combine(_dir, name))))
        using (var tmp = Image.FromStream(ms))
        {
            var old = _orig;
            _orig = new Bitmap(tmp);
            old?.Dispose();
        }
        SetImage(_orig);
        _factor = 1;
        ApplyZoom();
        var stem = Path.GetFileNameWithoutExtension(name);
        if (_codec.IsSkipped(stem))
        {
            for (var i = 0; i < _slots.Length; i++) SetSlotText(i, "");
            _status.Text = $"{name}（已跳过；直接保存可解除跳过）";
            return;
        }
        if (_codec.TryParse(stem, out _, out var names))
            for (var i = 0; i < _slots.Length; i++)
                SetSlotText(i, names[i] == null ? "?" : _db.TryExact(names[i]!, out var c) ? c.DisplayName : names[i]!);
        else
            for (var i = 0; i < _slots.Length; i++) SetSlotText(i, "");
        _status.Text = name;
    }

    private void SaveAndNext()
    {
        HidePopup();
        if (_list.SelectedItem is not string cur) return;
        var names = new string?[_cfg.Slots];
        var bad = new List<int>();
        for (var i = 0; i < _cfg.Slots; i++)
        {
            var t = _slots[i].Text.Trim();
            if (_codec.IsUnknown(t)) names[i] = null;
            else if (_db.TryExact(t, out var c)) names[i] = c.DisplayName;
            else bad.Add(i + 1);
        }
        if (bad.Count > 0)
        {
            MessageBox.Show(this, $"槽位 {string.Join(", ", bad)} 的卡名不在卡库中：请从自动完成候选中选择，或填 ? 表示未知。",
                "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var idx = _list.SelectedIndex;
        var newName = _store.ApplyLabels(_dir, cur, names);
        _status.Text = $"已保存: {newName}";
        AdvanceAfter(idx, newName);
    }

    private void SkipCurrent()
    {
        HidePopup();
        if (_list.SelectedItem is not string cur) return;
        var idx = _list.SelectedIndex;
        var newName = _store.Skip(_dir, cur);
        _status.Text = $"已跳过: {newName}";
        AdvanceAfter(idx, newName);
    }

    private void AdvanceAfter(int oldIdx, string newName)
    {
        var unlabeledFilter = _filter.SelectedIndex == 1;
        RefreshList(unlabeledFilter ? null : newName);
        if (_view.Count == 0) { _list.SelectedIndex = -1; return; }
        if (unlabeledFilter) _list.SelectedIndex = Math.Min(oldIdx, _view.Count - 1);
        else
        {
            var j = _view.IndexOf(newName);
            _list.SelectedIndex = j >= 0 ? Math.Min(j + 1, _view.Count - 1) : Math.Min(oldIdx, _view.Count - 1);
        }
    }

    private void Step(int d)
    {
        if (_view.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + d, 0, _view.Count - 1);
    }

    private void PasteSplit()
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        var parts = text.Split(_cfg.NameSeparator);
        if (parts.Length != _cfg.Slots)
        {
            MessageBox.Show(this, $"剪贴板不是 {_cfg.Slots} 个用 {_cfg.NameSeparator} 分隔的卡名。", "粘贴拆填",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        HidePopup();
        for (var i = 0; i < _cfg.Slots; i++) SetSlotText(i, parts[i].Trim());
        _status.Text = "已拆填 5 槽，请核对后 Enter 保存";
    }

    private Size FitSize()
    {
        var img = _pic.Image;
        if (img == null) return new Size(100, 100);
        var host = _picHost.ClientSize;
        var scale = Math.Min((double)host.Width / img.Width, (double)host.Height / img.Height);
        if (scale <= 0) scale = 1;
        return new Size(Math.Max(1, (int)(img.Width * scale)), Math.Max(1, (int)(img.Height * scale)));
    }

    private void ApplyZoom()
    {
        if (_pic.Image == null) return;
        var fit = FitSize();
        _pic.Size = new Size(Math.Max(1, (int)(fit.Width * _factor)), Math.Max(1, (int)(fit.Height * _factor)));
        _picHost.AutoScrollMinSize = _pic.Size;
        _zoomLabel.Text = $"缩放 {_factor:0.##}x";
    }

    private void SetFactor(double f)
    {
        _factor = Math.Clamp(f, 0.2, 8);
        ApplyZoom();
    }

    private void OneToOne()
    {
        SetImage(_orig);
        if (_orig == null) return;
        var fit = FitSize();
        _factor = fit.Width > 0 ? (double)_orig.Width / fit.Width : 1;
        ApplyZoom();
    }

    private void HandZoom()
    {
        if (_orig == null) return;
        var r = _cfg.HandRoi;
        var rect = new Rectangle((int)(r.X * _orig.Width), (int)(r.Y * _orig.Height),
            (int)(r.W * _orig.Width), (int)(r.H * _orig.Height));
        rect.Intersect(new Rectangle(0, 0, _orig.Width, _orig.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var crop = ((Bitmap)_orig).Clone(rect, _orig.PixelFormat);
        var bmp = new Bitmap(crop.Width * _cfg.Upscale, crop.Height * _cfg.Upscale);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(crop, 0, 0, bmp.Width, bmp.Height);
        }
        SetImage(bmp);
        _factor = 1;
        ApplyZoom();
    }

    private void SetImage(Image? img)
    {
        if (_pic.Image != null && !ReferenceEquals(_pic.Image, _orig)) _pic.Image.Dispose();
        _pic.Image = img;
    }

    private void PicMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragCursor = Cursor.Position;
        var p = _picHost.AutoScrollPosition;
        _dragScroll = new Point(-p.X, -p.Y);
    }

    private void PicMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var d = Cursor.Position - (Size)_dragCursor;
        _picHost.AutoScrollPosition = new Point(_dragScroll.X - d.X, _dragScroll.Y - d.Y);
    }
}