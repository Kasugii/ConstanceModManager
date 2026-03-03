using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ConstanceModManager
{


    // ── FlowLayoutPanel that redraws the background correctly ───────────────
    // Uses pre-darkened bitmaps (baked-in overlay) to avoid any
    // alpha compositing issues in OnPaintBackground / WM_ERASEBKGND.
    class BgFlowPanel : FlowLayoutPanel
    {
        public Bitmap[] DarkenedBgs;   // pre-darkened bitmaps, no alpha
        public Func<int> BgIndexGetter;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            int idx = BgIndexGetter != null ? BgIndexGetter() : 0;
            Bitmap bg = (DarkenedBgs != null && idx < DarkenedBgs.Length) ? DarkenedBgs[idx] : null;
            if (bg == null) { base.OnPaintBackground(e); return; }

            Control root = this.Parent;
            if (root == null) { base.OnPaintBackground(e); return; }

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            Point offset = this.Location;
            // Draw image at inverse offset to simulate transparency
            e.Graphics.DrawImage(bg, -offset.X, -offset.Y, root.Width, root.Height);
        }
    }

    public class MainForm : Form
    {
        // ── Paths ─────────────────────────────────────────────────────────────
        string GameDir { get { return string.IsNullOrEmpty(_cfg.GameExePath) ? "" : Path.GetDirectoryName(_cfg.GameExePath) ?? ""; } }
        string BepInExDir { get { return Path.Combine(GameDir, "BepInEx"); } }
        string PluginsDir { get { return Path.Combine(GameDir, "BepInEx", "plugins"); } }
        string ModsStorage { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mods"); } }

        // ── Color palette ─────────────────────────────────────────────────────
        static readonly Color CB = Color.FromArgb(10, 6, 20);   // background
        static readonly Color CC = Color.FromArgb(28, 18, 50);   // card
        static readonly Color CA = Color.FromArgb(150, 80, 245);   // purple
        static readonly Color CA2 = Color.FromArgb(200, 120, 255);   // light purple
        static readonly Color CG = Color.FromArgb(50, 190, 110);   // green
        static readonly Color CR = Color.FromArgb(210, 60, 60);   // red
        static readonly Color CY = Color.FromArgb(255, 185, 50);   // yellow
        static readonly Color CT = Color.FromArgb(230, 215, 255);   // text
        static readonly Color CM = Color.FromArgb(120, 100, 160);   // muted
        static readonly Color CBo = Color.FromArgb(55, 40, 85);   // border
        static readonly Color CSurface = Color.FromArgb(20, 13, 38);

        // ── State ─────────────────────────────────────────────────────────────
        Settings _cfg;
        List<ModEntry> _mods = new List<ModEntry>();
        NotifyIcon _tray;
        bool _forceQuit = false;
        Bitmap[] _bgs = new Bitmap[2];   // _bgs[0]=bg1, _bgs[1]=bg2
        Bitmap[] _darkenedBgs = new Bitmap[2];  // pre-darkened version (baked-in overlay)

        // ── UI refs ───────────────────────────────────────────────────────────
        Panel _root;
        Panel _settingsOverlay;
        BgFlowPanel _modList;
        Label _lblPath, _lblStatus;
        Button _btnPlay, _btnSettings;
        // Translated labels
        Label _lblTitle, _lblSubtitle, _lblFooter, _lblModsTitle, _lblDrop;
        Button _btnBrowse, _btnAdd;

        static readonly string[] LANG_CODES = { "fr", "en", "es", "zh", "hi" };
        static readonly string[] LANG_NAMES = { "Francais", "English", "Espanol", "Zhongwen", "Hindi" };

        // ════════════════════════════════════════════════════════════════════
        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96f, 96f);
            // ── Anti-lag resize: double buffer on the window itself ───────────
            this.DoubleBuffered = true;
            Directory.CreateDirectory(ModsStorage);
            _cfg = Settings.Load();
            L.Lang = _cfg.Language;
            // Auto-detect Steam if no path configured yet
            if (string.IsNullOrEmpty(_cfg.GameExePath))
            {
                string found = TryFindConstanceSteam();
                if (!string.IsNullOrEmpty(found))
                { _cfg.GameExePath = found; _cfg.Save(); }
            }
            _bgs[0] = LoadEmbeddedBitmap("bg1");
            _bgs[1] = LoadEmbeddedBitmap("bg2");
            // Fallback if embedded resources are not yet integrated
            if (_bgs[0] == null && _bgs[1] == null) _bgs[0] = MakeBg(900, 650);
            // Pre-compute darkened bitmaps once (baked-in overlay)
            // avoids alpha compositing at paint time (guaranteed glitch-free)
            for (int i = 0; i < _bgs.Length; i++)
                _darkenedBgs[i] = _bgs[i] != null ? BakeDarken(_bgs[i]) : null;
            BuildUI();
            BuildTray();
            RefreshMods();
        }

        // ════════════════════════════════════════════════════════════════════
        //  BUILD UI
        //  DockStyle rules:
        //    Fill   → add FIRST
        //    Bottom → after Fill
        //    Top    → bottom to top (last added = topmost)
        // ════════════════════════════════════════════════════════════════════
        void BuildUI()
        {
            Text = "Constance Mod Manager";
            ClientSize = new Size(860, 720);
            MinimumSize = new Size(640, 540);
            BackColor = CB;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => ImportDlls((string[])e.Data.GetData(DataFormats.FileDrop));

            _root = new Panel();
            _root.Dock = DockStyle.Fill;
            _root.BackColor = CB;
            _root.Paint += RootPaint;
            // ── Anti-lag: only redraw AT THE END of resize, not every pixel ──
            ResizeBegin += (s, e) => { this.SuspendLayout(); };
            ResizeEnd += (s, e) => { this.ResumeLayout(true); FitCards(); _root.Invalidate(); SizeOverlay(); };
            Controls.Add(_root);

            // ── 1. FILL ───────────────────────────────────────────────────────
            _modList = new BgFlowPanel
            {
                DarkenedBgs = _darkenedBgs,
                BgIndexGetter = () => _cfg.BgIndex,
            };
            _modList.Dock = DockStyle.Fill;
            _modList.FlowDirection = FlowDirection.TopDown;
            _modList.WrapContents = false;
            _modList.AutoScroll = true;
            _modList.BackColor = Color.Transparent;
            _modList.Padding = new Padding(10, 6, 10, 6);
            _modList.Resize += (s, e) => FitCards();
            _root.Controls.Add(_modList);

            // ── 2. BOTTOM ─────────────────────────────────────────────────────
            Panel footer = MkPanel(DockStyle.Bottom, 62, Color.FromArgb(230, 10, 6, 20));
            footer.Paint += (s, e) => HLine(e, footer.Width, Color.FromArgb(70, CBo), 0);
            _btnPlay = MkBtn("▶  " + L.Get("launch"), CG, 0, 44);
            _btnPlay.AutoSize = true;
            _btnPlay.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnPlay.Padding = new Padding(24, 0, 24, 0);
            _btnPlay.Dock = DockStyle.Right;
            _btnPlay.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnPlay.Click += DoLaunch;
            _lblFooter = MkLbl(L.Get("footer"), new Font("Segoe UI", 8f), CM);
            Label lf = _lblFooter;
            lf.AutoSize = true; lf.Location = new Point(14, 20);
            footer.Controls.Add(_btnPlay);
            footer.Controls.Add(lf);
            _root.Controls.Add(footer);

            // ── 3. TOP bottom to top ──────────────────────────────────────────

            // Drop zone
            Panel dz = MkPanel(DockStyle.Top, 52, Color.FromArgb(140, 8, 5, 16));
            dz.AllowDrop = true;
            dz.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            dz.DragDrop += (s, e) => ImportDlls((string[])e.Data.GetData(DataFormats.FileDrop));
            dz.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Pen p = new Pen(Color.FromArgb(50, CBo), 1); p.DashStyle = DashStyle.Dash;
                RoundRect(e.Graphics, p, new Rectangle(10, 5, dz.Width - 22, dz.Height - 10), 7);
                p.Dispose();
            };
            _lblDrop = MkLbl(L.Get("drop"), new Font("Segoe UI", 8.5f, FontStyle.Italic), Color.FromArgb(75, CM.R, CM.G, CM.B));
            Label dzL = _lblDrop;
            dzL.Dock = DockStyle.Fill; dzL.TextAlign = ContentAlignment.MiddleCenter;
            dzL.AllowDrop = true;
            dzL.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            dzL.DragDrop += (s, e) => ImportDlls((string[])e.Data.GetData(DataFormats.FileDrop));
            dz.Controls.Add(dzL);
            _root.Controls.Add(dz);

            // Mods section bar
            Panel sec = MkPanel(DockStyle.Top, 46, Color.FromArgb(165, 14, 9, 26));
            sec.Paint += (s, e) => HLine(e, sec.Width, Color.FromArgb(55, CBo), sec.Height - 1);
            _lblModsTitle = MkLbl(L.Get("mods_title"), new Font("Segoe UI", 7.5f, FontStyle.Bold), CM);
            _lblModsTitle.AutoSize = true; _lblModsTitle.Location = new Point(14, 15);
            Label lMods = _lblModsTitle;
            _btnAdd = MkBtn(L.Get("add"), CA, 0, 34);
            _btnAdd.AutoSize = true;
            _btnAdd.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnAdd.Padding = new Padding(14, 0, 14, 0);
            Button btnAdd = _btnAdd;
            btnAdd.Click += (s, e) => {
                OpenFileDialog d = new OpenFileDialog();
                d.Title = L.Get("select_dll"); d.Filter = "DLL|*.dll"; d.Multiselect = true;
                if (d.ShowDialog() == DialogResult.OK) ImportDlls(d.FileNames);
            };
            sec.Controls.Add(lMods); sec.Controls.Add(btnAdd);
            // Reposition on parent resize AND on button size change
            Action placeAdd = () => {
                if (sec.Width > 0)
                    btnAdd.Location = new Point(sec.Width - btnAdd.Width - 12, (sec.Height - btnAdd.Height) / 2);
            };
            sec.Resize += (s, e) => placeAdd();
            btnAdd.Resize += (s, e) => placeAdd(); // AutoSize change → immediate reposition
            sec.PerformLayout();
            _root.Controls.Add(sec);

            // Status bar
            Panel statusBar = MkPanel(DockStyle.Top, 34, Color.FromArgb(160, 10, 6, 18));
            _lblStatus = MkLbl("", new Font("Segoe UI", 8.5f), CM);
            _lblStatus.AutoSize = false;
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Padding = new Padding(14, 0, 0, 0);
            statusBar.Controls.Add(_lblStatus);
            _root.Controls.Add(statusBar);

            // Game path bar — taller, AutoEllipsis if path is very long
            Panel pathBar = MkPanel(DockStyle.Top, 56, Color.FromArgb(185, 10, 6, 18));
            pathBar.Paint += (s, e) => HLine(e, pathBar.Width, Color.FromArgb(40, CBo), pathBar.Height - 1);
            _lblPath = MkLbl(L.Get("no_game"), new Font("Segoe UI", 9f), CM);
            _lblPath.AutoSize = false;
            _lblPath.Height = 26;
            _lblPath.Location = new Point(14, 14);
            _lblPath.AutoEllipsis = true;
            _lblPath.TextAlign = ContentAlignment.MiddleLeft;
            _btnBrowse = MkBtn("📁 " + L.Get("browse"), CA, 0, 36);
            _btnBrowse.AutoSize = true;
            _btnBrowse.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnBrowse.Padding = new Padding(16, 0, 16, 0);
            _btnBrowse.Click += DoBrowse;
            Button btnBrowse = _btnBrowse;
            pathBar.Controls.Add(_lblPath); pathBar.Controls.Add(btnBrowse);
            // Reposition on parent resize AND on button size change
            Action placeBrowse = () => {
                if (pathBar.Width > 0)
                {
                    btnBrowse.Location = new Point(pathBar.Width - btnBrowse.Width - 14, (pathBar.Height - btnBrowse.Height) / 2);
                    _lblPath.Width = btnBrowse.Left - 14 - _lblPath.Left;
                }
            };
            pathBar.Resize += (s, e) => placeBrowse();
            btnBrowse.Resize += (s, e) => placeBrowse(); // AutoSize change → immediate reposition
            pathBar.PerformLayout();
            _root.Controls.Add(pathBar);

            // Settings overlay — fullscreen over _root
            _settingsOverlay = BuildSettingsOverlay();
            _settingsOverlay.Visible = false;
            _root.Controls.Add(_settingsOverlay);

            // ── 4. HEADER — last added = topmost ─────────────────────────────
            Panel hdr = MkPanel(DockStyle.Top, 92, Color.FromArgb(220, 14, 8, 26));
            hdr.Paint += (s, e) => {
                LinearGradientBrush lb = new LinearGradientBrush(
                    new Point(0, 0), new Point(hdr.Width, 0), CA, CA2);
                e.Graphics.FillRectangle(lb, 0, 0, hdr.Width, 3); lb.Dispose();
                HLine(e, hdr.Width, Color.FromArgb(55, CBo), hdr.Height - 1);
            };
            _lblTitle = MkLbl(L.Get("title"), new Font("Segoe UI", 12f, FontStyle.Bold), CA2);
            Label lTitle = _lblTitle;
            lTitle.AutoSize = true; lTitle.Location = new Point(16, 16);
            _lblSubtitle = MkLbl(L.Get("subtitle"), new Font("Segoe UI", 8f), CM);
            Label lSub = _lblSubtitle;
            lSub.AutoSize = true; lSub.Location = new Point(18, 54);
            _btnSettings = MkBtn("⚙ " + L.Get("settings"), Color.FromArgb(40, 255, 255, 255), 0, 36);
            _btnSettings.AutoSize = true;
            _btnSettings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnSettings.Padding = new Padding(14, 0, 14, 0);
            _btnSettings.ForeColor = CM;
            _btnSettings.FlatAppearance.BorderColor = Color.FromArgb(65, CBo);
            _btnSettings.FlatAppearance.BorderSize = 1;
            _btnSettings.Click += ToggleSettings;
            hdr.Controls.Add(lTitle); hdr.Controls.Add(lSub); hdr.Controls.Add(_btnSettings);
            // Reposition on parent resize AND on button size change (AutoSize / language)
            Action placeSettings = () => {
                if (hdr.Width > 0)
                    _btnSettings.Location = new Point(hdr.Width - _btnSettings.Width - 14, (hdr.Height - _btnSettings.Height) / 2);
            };
            hdr.Resize += (s, e) => placeSettings();
            _btnSettings.Resize += (s, e) => placeSettings(); // AutoSize change → immediate reposition
            hdr.PerformLayout();
            _root.Controls.Add(hdr);

            if (!string.IsNullOrEmpty(_cfg.GameExePath))
            { _lblPath.Text = _cfg.GameExePath; _lblPath.ForeColor = CT; }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SETTINGS OVERLAY — fullscreen, hides everything else
        // ════════════════════════════════════════════════════════════════════
        Panel BuildSettingsOverlay()
        {
            Panel ov = new Panel { BackColor = Color.FromArgb(250, 10, 6, 20) };
            ov.Paint += (s, e) => {
                LinearGradientBrush lb = new LinearGradientBrush(new Point(0, 0), new Point(ov.Width, 0), CA, CA2);
                e.Graphics.FillRectangle(lb, 0, 0, ov.Width, 3); lb.Dispose();
                Pen p = new Pen(Color.FromArgb(55, CBo));
                e.Graphics.DrawLine(p, 16, 78, ov.Width - 16, 78); p.Dispose();
            };

            Label lT = MkLbl(L.Get("settings_title"), new Font("Segoe UI", 13f, FontStyle.Bold), CA2);
            lT.AutoSize = true; lT.Location = new Point(20, 22);

            Button btnBack = MkBtn(L.Get("back"), Color.FromArgb(50, 50, 80), 0, 34);
            btnBack.AutoSize = true; btnBack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnBack.Padding = new Padding(14, 0, 14, 0);
            btnBack.Click += (s, e) => CloseSettings();
            ov.Controls.Add(btnBack);
            ov.Resize += (s, e2) => btnBack.Location = new Point(ov.Width - btnBack.Width - 16, 22);

            // ─ Console ────────────────────────────────────────────────────────
            Label lConsHead = MkLbl(L.Get("console_opt"), new Font("Segoe UI", 10f), CT);
            lConsHead.AutoSize = true; lConsHead.Location = new Point(50, 100);

            CheckBox chk = new CheckBox();
            chk.Checked = _cfg.ShowBepInExConsole; chk.BackColor = Color.Transparent;
            chk.Size = new Size(22, 22); chk.Location = new Point(18, 100); chk.Cursor = Cursors.Hand;
            chk.CheckedChanged += (s, e) => { _cfg.ShowBepInExConsole = chk.Checked; _cfg.Save(); ApplyBepConfig(); };

            // ─ Language ───────────────────────────────────────────────────────
            Panel sep = new Panel { BackColor = Color.FromArgb(45, CBo), Height = 1, Location = new Point(16, 148) };
            ov.Resize += (s, e2) => sep.Width = ov.Width - 32;

            Label lLH = MkLbl(L.Get("language_lbl"), new Font("Segoe UI", 10f, FontStyle.Bold), CT);
            lLH.AutoSize = true; lLH.Location = new Point(18, 164);

            int lx = 18, ly = 200;
            string[] codes = { "fr", "en", "es", "zh", "hi" };
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i]; bool active = _cfg.Language == code;
                string displayName = L.Get("lang_" + code);
                Button b = MkBtn(displayName, active ? CA : Color.FromArgb(50, 44, 80), 0, 36);
                b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.Padding = new Padding(16, 0, 16, 0); b.Location = new Point(lx, ly); b.Tag = code;
                b.FlatAppearance.BorderSize = active ? 1 : 0;
                b.FlatAppearance.BorderColor = Color.FromArgb(120, CA);
                b.Click += (s, e2) => {
                    string sel = (string)((Button)s).Tag;
                    _cfg.Language = sel; L.Lang = sel; _cfg.Save();
                    // Rebuild overlay to refresh active buttons
                    _root.Controls.Remove(_settingsOverlay);
                    _settingsOverlay.Dispose();
                    _settingsOverlay = BuildSettingsOverlay();
                    _root.Controls.Add(_settingsOverlay);
                    _settingsOverlay.BringToFront();
                    SizeOverlay(); _settingsOverlay.Visible = true;
                    RefreshTexts();
                };
                ov.Controls.Add(b); lx += b.Width + 10;
                if (lx + 120 > 800) { lx = 18; ly += 46; }
            }

            ov.Controls.Add(lT); ov.Controls.Add(chk); ov.Controls.Add(lConsHead);
            ov.Controls.Add(sep); ov.Controls.Add(lLH);

            // ─ Background ─────────────────────────────────────────────────────
            int bgSectionY = ly + 56;

            Panel sep2 = new Panel { BackColor = Color.FromArgb(45, CBo), Height = 1 };
            sep2.Location = new Point(16, bgSectionY);
            ov.Resize += (s, e2) => { sep2.Width = ov.Width - 32; };

            Label lBgH = MkLbl(L.Get("bg_lbl"), new Font("Segoe UI", 10f, FontStyle.Bold), CT);
            lBgH.AutoSize = true; lBgH.Location = new Point(18, bgSectionY + 16);

            string[] bgNames = { L.Get("bg1_name"), L.Get("bg2_name") };
            int bgX = 18, bgY = bgSectionY + 52;
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                bool bgActive = _cfg.BgIndex == idx;

                // Thumbnail 80x50 with dominant color preview
                Panel thumb = new Panel();
                thumb.Size = new Size(90, 56);
                thumb.Location = new Point(bgX, bgY);
                thumb.Cursor = Cursors.Hand;
                thumb.BackColor = idx == 0
                    ? Color.FromArgb(80, 60, 90)   // bg1 tint (dark purple)
                    : Color.FromArgb(90, 65, 30);  // bg2 tint (warm gold)

                // Active border
                thumb.Paint += (s, e2) => {
                    bool isActive = _cfg.BgIndex == idx;
                    Pen pen = new Pen(isActive ? CA2 : Color.FromArgb(50, CBo), isActive ? 2f : 1f);
                    e2.Graphics.DrawRectangle(pen, 0, 0, thumb.Width - 1, thumb.Height - 1);
                    pen.Dispose();
                    // Draw thumbnail if image is loaded
                    Bitmap bmp = _bgs[idx];
                    if (bmp != null)
                    {
                        e2.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        e2.Graphics.DrawImage(bmp, 1, 1, thumb.Width - 2, thumb.Height - 2);
                    }
                    // Dark overlay if inactive
                    if (!isActive)
                    {
                        SolidBrush dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
                        e2.Graphics.FillRectangle(dim, 1, 1, thumb.Width - 2, thumb.Height - 2);
                        dim.Dispose();
                    }
                };

                thumb.Click += (s, e2) => {
                    _cfg.BgIndex = idx; _cfg.Save();
                    _root.Invalidate();
                    // Rebuild overlay to refresh borders
                    _root.Controls.Remove(_settingsOverlay);
                    _settingsOverlay.Dispose();
                    _settingsOverlay = BuildSettingsOverlay();
                    _root.Controls.Add(_settingsOverlay);
                    _settingsOverlay.BringToFront();
                    SizeOverlay(); _settingsOverlay.Visible = true;
                };

                Label lBgName = MkLbl(bgNames[idx], new Font("Segoe UI", 8f), bgActive ? CT : CM);
                lBgName.AutoSize = true;
                lBgName.Location = new Point(bgX, bgY + 60);

                ov.Controls.Add(thumb);
                ov.Controls.Add(lBgName);
                bgX += 110;
            }

            ov.Controls.Add(sep2); ov.Controls.Add(lBgH);
            return ov;
        }

        void SizeOverlay()
        {
            if (_settingsOverlay == null || _root == null) return;
            _settingsOverlay.Bounds = new Rectangle(0, 0, _root.Width, _root.Height);
        }

        void OpenSettings()
        {
            SizeOverlay();
            _settingsOverlay.Visible = true;
            _settingsOverlay.BringToFront();
            _btnSettings.BackColor = Color.FromArgb(70, CA);
            _btnSettings.ForeColor = Color.White;
        }

        void CloseSettings()
        {
            _settingsOverlay.Visible = false;
            _btnSettings.BackColor = Color.FromArgb(40, 255, 255, 255);
            _btnSettings.ForeColor = CM;
        }

        void ToggleSettings(object s, EventArgs e)
        {
            if (_settingsOverlay.Visible) CloseSettings(); else OpenSettings();
        }

        void RefreshTexts()
        {
            // Header
            _lblTitle.Text = L.Get("title");
            _lblSubtitle.Text = L.Get("subtitle");
            // Main buttons
            _btnSettings.Text = "⚙ " + L.Get("settings");
            _btnPlay.Text = "▶  " + L.Get("launch");
            _btnBrowse.Text = "📁 " + L.Get("browse");
            _btnAdd.Text = L.Get("add");
            // Labels
            _lblFooter.Text = L.Get("footer");
            _lblModsTitle.Text = L.Get("mods_title");
            _lblDrop.Text = L.Get("drop");
            if (string.IsNullOrEmpty(_cfg.GameExePath)) _lblPath.Text = L.Get("no_game");
            RebuildTrayMenu();
            DrawModList();
            UpdateStatus();
        }

        // ════════════════════════════════════════════════════════════════════
        //  BACKGROUND
        // ════════════════════════════════════════════════════════════════════
        // Load an embedded image (Build Action = Embedded Resource)
        // Expected name: "bg1" or "bg2"
        static Bitmap LoadEmbeddedBitmap(string baseName)
        {
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string rn in asm.GetManifestResourceNames())
                {
                    string low = rn.ToLowerInvariant();
                    if (low.Contains(baseName.ToLowerInvariant()) &&
                        (low.EndsWith(".png") || low.EndsWith(".jpg") || low.EndsWith(".jpeg")))
                    {
                        using (Stream st = asm.GetManifestResourceStream(rn))
                            return new Bitmap(st);
                    }
                }
            }
            catch { }
            return null;
        }

        // Procedural fallback if no image is found
        static Bitmap MakeBg(int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h);
            Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            LinearGradientBrush bg = new LinearGradientBrush(new Point(0, 0), new Point(w, h),
                Color.FromArgb(10, 5, 22), Color.FromArgb(22, 9, 40));
            g.FillRectangle(bg, 0, 0, w, h); bg.Dispose();
            Random rnd = new Random(7);
            Color[] cols = { Color.FromArgb(16,110,55,210), Color.FromArgb(12,65,18,150),
                             Color.FromArgb(14,130,70,210), Color.FromArgb(10,55,170,100) };
            for (int i = 0; i < 14; i++)
            {
                Color c = cols[rnd.Next(cols.Length)];
                int bx = rnd.Next(-80, w + 80), by = rnd.Next(-80, h + 80),
                    bw = rnd.Next(80, 260), bh = (int)(bw * (0.5 + rnd.NextDouble() * 0.8));
                SolidBrush br = new SolidBrush(c); g.FillEllipse(br, bx, by, bw, bh); br.Dispose();
            }
            GraphicsPath vp = new GraphicsPath();
            vp.AddEllipse(-w / 3, -h / 3, w + w * 2 / 3, h + h * 2 / 3);
            PathGradientBrush vb = new PathGradientBrush(vp);
            vb.CenterColor = Color.FromArgb(0, 0, 0, 0);
            vb.SurroundColors = new Color[] { Color.FromArgb(165, 0, 0, 0) };
            g.FillRectangle(vb, 0, 0, w, h); vb.Dispose(); vp.Dispose();
            g.Dispose();
            return bmp;
        }

        void RootPaint(object sender, PaintEventArgs e)
        {
            // Use pre-darkened bitmap — no alpha overlay needed at paint time
            e.Graphics.InterpolationMode = InterpolationMode.Low;
            int idx = _cfg.BgIndex < _darkenedBgs.Length ? _cfg.BgIndex : 0;
            Bitmap bg = _darkenedBgs[idx] ?? _darkenedBgs[0] ?? _darkenedBgs[1];
            if (bg != null) e.Graphics.DrawImage(bg, 0, 0, _root.Width, _root.Height);
            else { using (SolidBrush br = new SolidBrush(CB)) e.Graphics.FillRectangle(br, 0, 0, _root.Width, _root.Height); }
        }

        // ════════════════════════════════════════════════════════════════════
        //  TRAY
        // ════════════════════════════════════════════════════════════════════
        void BuildTray()
        {
            Icon appIcon = LoadEmbeddedIcon();
            if (appIcon != null) this.Icon = appIcon;

            _tray = new NotifyIcon();
            _tray.Text = "Constance Mod Manager";
            _tray.Icon = appIcon ?? SystemIcons.Application;
            _tray.Visible = true;
            RebuildTrayMenu();
            _tray.DoubleClick += (s, e) => Restore();
        }

        void RebuildTrayMenu()
        {
            if (_tray.ContextMenuStrip != null) _tray.ContextMenuStrip.Dispose();
            ContextMenuStrip m = new ContextMenuStrip();
            ToolStripMenuItem mO = new ToolStripMenuItem(L.Get("tray_open"));
            mO.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            mO.Click += (s, e) => Restore();
            ToolStripMenuItem mL = new ToolStripMenuItem(L.Get("tray_launch")); mL.Click += DoLaunch;
            ToolStripMenuItem mQ = new ToolStripMenuItem(L.Get("tray_quit"));
            mQ.Click += (s, e) => { _forceQuit = true; Application.Exit(); };
            m.Items.Add(mO); m.Items.Add(new ToolStripSeparator());
            m.Items.Add(mL); m.Items.Add(new ToolStripSeparator()); m.Items.Add(mQ);
            _tray.ContextMenuStrip = m;
        }

        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        // Capture maximize / restore (WM_SIZE) not covered by ResizeBegin/End
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_SIZE = 0x0005;
            const int SIZE_MAXIMIZED = 2;
            const int SIZE_RESTORED = 0;
            if (m.Msg == WM_SIZE)
            {
                int type = m.WParam.ToInt32();
                if (type == SIZE_MAXIMIZED || type == SIZE_RESTORED)
                {
                    FitCards();
                    if (_root != null) _root.Invalidate();
                    SizeOverlay();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceQuit && e.CloseReason == CloseReason.UserClosing)
            { e.Cancel = true; Hide(); return; }
            _tray.Visible = false; _tray.Dispose();
            base.OnFormClosing(e);
        }

        // ════════════════════════════════════════════════════════════════════
        //  MODS
        // ════════════════════════════════════════════════════════════════════
        void RefreshMods()
        {
            _mods.Clear();
            foreach (string dll in Directory.GetFiles(ModsStorage, "*.dll"))
            {
                string fn = Path.GetFileName(dll);
                _mods.Add(new ModEntry
                {
                    Name = NiceName(Path.GetFileNameWithoutExtension(fn)),
                    FileName = fn,
                    StoredPath = dll,
                    Enabled = _cfg.EnabledMods.Contains(fn),
                    Version = DllVer(dll),
                });
            }
            DrawModList();
            UpdateStatus();
        }

        void DrawModList()
        {
            _modList.SuspendLayout();
            _modList.Controls.Clear();
            if (_mods.Count == 0)
            {
                Label e = MkLbl(L.Get("no_mods"), new Font("Segoe UI", 9f, FontStyle.Italic),
                    Color.FromArgb(65, CM.R, CM.G, CM.B));
                e.AutoSize = false;
                e.TextAlign = ContentAlignment.MiddleCenter;
                e.Dock = DockStyle.Fill;
                _modList.Controls.Add(e);
            }
            else foreach (ModEntry m in _mods) _modList.Controls.Add(MakeCard(m));
            _modList.ResumeLayout();
            if (IsHandleCreated) BeginInvoke(new Action(FitCards));
            else FitCards();
        }

        void FitCards()
        {
            if (_modList == null || !IsHandleCreated) return;
            int w = _modList.ClientSize.Width - 4;
            if (w < 100) return;
            foreach (Control c in _modList.Controls) c.Width = w;
        }

        // ════════════════════════════════════════════════════════════════════
        //  MOD CARD with toggle pill
        // ════════════════════════════════════════════════════════════════════
        Control MakeCard(ModEntry mod)
        {
            int w = Math.Max(_modList.ClientSize.Width - 4, 320);

            Panel card = new Panel();
            card.Width = w;
            card.Height = 92;
            card.BackColor = CC;
            card.Margin = new Padding(0, 0, 0, 6);
            card.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Pen p = new Pen(Color.FromArgb(60, CBo), 1);
                RoundRect(e.Graphics, p, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 7);
                p.Dispose();
            };

            // Colored side bar
            Panel bar = new Panel();
            bar.Dock = DockStyle.Left;
            bar.Width = 5;
            bar.BackColor = mod.Enabled ? CG : CBo;

            // Mod name
            Label lName = MkLbl(mod.Name, new Font("Segoe UI", 9.5f, FontStyle.Bold), CT);
            lName.AutoSize = true;
            lName.Location = new Point(16, 10);

            // Version + filename — larger area, readable font, truncation with "..."
            Label lSub = MkLbl("v" + mod.Version + "   " + mod.FileName,
                new Font("Segoe UI", 9.5f), Color.FromArgb(155, CM.R, CM.G, CM.B));
            lSub.AutoSize = false;
            lSub.Height = 30;
            lSub.Location = new Point(16, 46);
            lSub.AutoEllipsis = true;

            // ── Toggle pill ───────────────────────────────────────────────────
            // Hand-drawn: rounded background + circle + ON/OFF text
            int togW = 82, togH = 32;
            Panel tog = new Panel();
            tog.Size = new Size(togW, togH);
            tog.BackColor = Color.Transparent;
            tog.Cursor = Cursors.Hand;
            tog.Tag = mod.Enabled;

            tog.Paint += (s, e) => {
                bool on = (bool)tog.Tag;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                Rectangle track = new Rectangle(0, 4, togW, togH - 8);
                Color trackCol = on ? Color.FromArgb(55, CG.R, CG.G, CG.B)
                                     : Color.FromArgb(40, 80, 60, 120);
                Color borderCol = on ? Color.FromArgb(160, CG.R, CG.G, CG.B)
                                     : Color.FromArgb(80, CBo.R, CBo.G, CBo.B);
                FillPill(g, trackCol, track);
                DrawPill(g, new Pen(borderCol, 1.5f), track);

                int knobSize = togH - 10;
                int knobX = on ? togW - knobSize - 5 : 5;
                int knobY = (togH - knobSize) / 2;
                SolidBrush shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
                g.FillEllipse(shadow, knobX + 1, knobY + 1, knobSize, knobSize); shadow.Dispose();
                Color knobCol = on ? CG : Color.FromArgb(180, 150, 130, 200);
                SolidBrush kb = new SolidBrush(knobCol);
                g.FillEllipse(kb, knobX, knobY, knobSize, knobSize); kb.Dispose();
                SolidBrush shine = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
                g.FillEllipse(shine, knobX + 3, knobY + 2, knobSize / 2, knobSize / 3); shine.Dispose();

                string txt = on ? "ON" : "OFF";
                Color txtCol = on ? Color.FromArgb(180, CG.R, CG.G, CG.B) : Color.FromArgb(100, CM.R, CM.G, CM.B);
                Font tf = new Font("Segoe UI", 7f, FontStyle.Bold);
                StringFormat sf = new StringFormat();
                sf.Alignment = on ? StringAlignment.Near : StringAlignment.Far;
                sf.LineAlignment = StringAlignment.Center;
                int txtPad = knobSize + 8;
                RectangleF txtRect = on
                    ? new RectangleF(txtPad, 0, togW - txtPad - 2, togH)
                    : new RectangleF(2, 0, togW - txtPad - 2, togH);
                SolidBrush tb = new SolidBrush(txtCol);
                g.DrawString(txt, tf, tb, txtRect, sf);
                tf.Dispose(); tb.Dispose();
            };

            tog.Click += (s, e) => {
                mod.Enabled = !mod.Enabled;
                tog.Tag = mod.Enabled;
                bar.BackColor = mod.Enabled ? CG : CBo;
                // Immediately save state
                if (mod.Enabled) { if (!_cfg.EnabledMods.Contains(mod.FileName)) _cfg.EnabledMods.Add(mod.FileName); }
                else { _cfg.EnabledMods.Remove(mod.FileName); }
                _cfg.Save();
                tog.Invalidate();
                UpdateStatus();
            };

            // Delete button — red circle, clean and discreet
            Panel del = new Panel();
            del.Size = new Size(28, 28);
            del.BackColor = Color.Transparent;
            del.Cursor = Cursors.Hand;
            del.Tag = mod;
            del.Paint += (s, e) => {
                bool hov = del.ClientRectangle.Contains(del.PointToClient(Cursor.Position));
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color bg2 = hov ? Color.FromArgb(190, 180, 40, 40) : Color.FromArgb(70, CR.R, CR.G, CR.B);
                g.FillEllipse(new SolidBrush(bg2), 1, 1, del.Width - 3, del.Height - 3);
                g.DrawEllipse(new Pen(Color.FromArgb(hov ? 200 : 100, CR), 1f), 1, 1, del.Width - 3, del.Height - 3);
                Pen xp = new Pen(Color.FromArgb(hov ? 255 : 190, 240, 80, 80), 1.8f);
                xp.StartCap = LineCap.Round; xp.EndCap = LineCap.Round;
                int m2 = 8;
                g.DrawLine(xp, m2, m2, del.Width - m2, del.Height - m2);
                g.DrawLine(xp, del.Width - m2, m2, m2, del.Height - m2);
                xp.Dispose();
            };
            del.MouseEnter += (s, e) => del.Invalidate();
            del.MouseLeave += (s, e) => del.Invalidate();
            del.Click += (s, e) => {
                ModEntry m = (ModEntry)((Panel)s).Tag;
                if (MessageBox.Show(string.Format(L.Get("del_confirm"), m.Name),
                        L.Get("confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    string dst = Path.Combine(PluginsDir, m.FileName);
                    if (File.Exists(dst)) try { File.Delete(dst); } catch { }
                    if (File.Exists(m.StoredPath)) try { File.Delete(m.StoredPath); } catch { }
                    _mods.Remove(m); _cfg.EnabledMods.Remove(m.FileName); _cfg.Save();
                    DrawModList(); UpdateStatus();
                }
            };

            card.Controls.Add(bar); card.Controls.Add(lName);
            card.Controls.Add(lSub); card.Controls.Add(tog); card.Controls.Add(del);

            Action place = () => {
                del.Location = new Point(card.Width - del.Width - 12, (card.Height - del.Height) / 2);
                tog.Location = new Point(del.Left - tog.Width - 10, (card.Height - tog.Height) / 2);
                lSub.Width = tog.Left - lSub.Left - 8;
            };
            card.Resize += (s, e) => place();
            place();
            return card;
        }

        // ════════════════════════════════════════════════════════════════════
        //  STATUS
        // ════════════════════════════════════════════════════════════════════
        void UpdateStatus()
        {
            bool hasGame = !string.IsNullOrEmpty(GameDir) && Directory.Exists(GameDir);
            bool hasBep = hasGame && Directory.Exists(BepInExDir);
            int active = _mods.Count(m => m.Enabled);
            if (!hasGame)
            {
                _lblStatus.Text = L.Get("status_sel");
                _lblStatus.ForeColor = CY;
                _btnPlay.Enabled = false;
                _btnPlay.BackColor = CM;
            }
            else if (hasBep)
            {
                _lblStatus.Text = string.Format(L.Get("status_ok"), active);
                _lblStatus.ForeColor = CG;
                _btnPlay.Enabled = true;
                _btnPlay.BackColor = CG;
            }
            else
            {
                _lblStatus.Text = L.Get("status_bep");
                _lblStatus.ForeColor = CA;
                _btnPlay.Enabled = true;
                _btnPlay.BackColor = CA;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ACTIONS
        // ════════════════════════════════════════════════════════════════════
        void DoBrowse(object s, EventArgs e)
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Title = L.Get("select_game");
            d.Filter = "Executable|*.exe";
            if (!string.IsNullOrEmpty(_cfg.GameExePath))
                d.InitialDirectory = Path.GetDirectoryName(_cfg.GameExePath) ?? "";
            if (d.ShowDialog() != DialogResult.OK) return;
            _cfg.GameExePath = d.FileName; _cfg.Save();
            _lblPath.Text = d.FileName;
            _lblPath.ForeColor = CT;
            ApplyBepConfig(); UpdateStatus();
        }

        void ImportDlls(string[] files)
        {
            foreach (string f in files)
            {
                if (!f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                string fn = Path.GetFileName(f);
                string dest = Path.Combine(ModsStorage, fn);
                try { File.Copy(f, dest, true); }
                catch (Exception ex) { MessageBox.Show("Error: " + fn + "\n" + ex.Message); continue; }
                ModEntry ex2 = _mods.FirstOrDefault(m => m.FileName == fn);
                if (ex2 != null) { ex2.StoredPath = dest; ex2.Version = DllVer(dest); }
                else _mods.Add(new ModEntry
                {
                    Name = NiceName(Path.GetFileNameWithoutExtension(fn)),
                    FileName = fn,
                    StoredPath = dest,
                    Enabled = false,
                    Version = DllVer(dest)
                });
            }
            DrawModList(); UpdateStatus();
        }

        void DoLaunch(object s, EventArgs e)
        {
            if (string.IsNullOrEmpty(_cfg.GameExePath) || !File.Exists(_cfg.GameExePath))
            { MessageBox.Show(L.Get("select_game"), "Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            if (!Directory.Exists(BepInExDir))
            {
                _lblStatus.Text = "Installing BepInEx...";
                _lblStatus.ForeColor = CA;
                _lblStatus.Refresh();
                Application.DoEvents();
                if (!InstallBepInEx()) return;
            }
            ApplyBepConfig();
            Directory.CreateDirectory(PluginsDir);

            // Sync enabled DLLs
            _cfg.EnabledMods.Clear();
            foreach (ModEntry m in _mods)
            {
                string dst = Path.Combine(PluginsDir, m.FileName);
                if (m.Enabled)
                {
                    try { File.Copy(m.StoredPath, dst, true); _cfg.EnabledMods.Add(m.FileName); }
                    catch (Exception ex) { MessageBox.Show("Error: " + m.FileName + "\n" + ex.Message); }
                }
                else { if (File.Exists(dst)) try { File.Delete(dst); } catch { } }
            }
            _cfg.Save();

            try
            {
                Process.Start(new ProcessStartInfo(_cfg.GameExePath) { UseShellExecute = true });
                _lblStatus.Text = L.Get("launched");
                _lblStatus.ForeColor = CG;
                Hide();
            }
            catch (Exception ex)
            { MessageBox.Show("Could not launch:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ════════════════════════════════════════════════════════════════════
        //  BEPINEX
        // ════════════════════════════════════════════════════════════════════
        bool InstallBepInEx()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string rn = null;
                foreach (string n in asm.GetManifestResourceNames())
                    if (n.EndsWith("BepInEx_x64.zip", StringComparison.OrdinalIgnoreCase)) { rn = n; break; }
                if (rn == null) throw new Exception(L.Get("bep_missing"));

                Stream st = asm.GetManifestResourceStream(rn);
                string tmp = Path.Combine(Path.GetTempPath(), "bep_install.zip");
                using (FileStream fs = File.Create(tmp)) st.CopyTo(fs);
                st.Dispose();

                using (ZipArchive zip = ZipFile.OpenRead(tmp))
                    foreach (ZipArchiveEntry en in zip.Entries)
                    {
                        if (en.FullName.EndsWith("/") || en.FullName.EndsWith("\\")) continue;
                        string dst = Path.Combine(GameDir, en.FullName.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        en.ExtractToFile(dst, true);
                    }
                File.Delete(tmp);
                if (!Directory.Exists(BepInExDir)) throw new Exception("BepInEx folder not found after extraction.");
                _lblStatus.Text = "BepInEx installed!"; _lblStatus.ForeColor = CG;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(L.Get("bep_error"), ex.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStatus.Text = "BepInEx error"; _lblStatus.ForeColor = CR;
                return false;
            }
        }

        void ApplyBepConfig()
        {
            if (string.IsNullOrEmpty(GameDir)) return;
            try
            {
                string cfgDir = Path.Combine(GameDir, "BepInEx", "config");
                Directory.CreateDirectory(cfgDir);
                string cfgPath = Path.Combine(cfgDir, "BepInEx.cfg");
                string val = _cfg.ShowBepInExConsole ? "true" : "false";
                if (File.Exists(cfgPath))
                {
                    string[] lines = File.ReadAllLines(cfgPath);
                    bool inSec = false, found = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = lines[i].Trim();
                        if (t == "[Logging.Console]") { inSec = true; continue; }
                        if (t.StartsWith("[") && inSec) inSec = false;
                        if (inSec && t.StartsWith("Enabled")) { lines[i] = "Enabled = " + val; found = true; }
                    }
                    if (!found)
                    {
                        List<string> l2 = new List<string>(lines);
                        l2.Add(""); l2.Add("[Logging.Console]"); l2.Add("Enabled = " + val);
                        File.WriteAllLines(cfgPath, l2.ToArray());
                    }
                    else File.WriteAllLines(cfgPath, lines);
                }
                else File.WriteAllText(cfgPath, "[Logging.Console]\nEnabled = " + val + "\n");
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════
        //  UI UTILITIES
        // ════════════════════════════════════════════════════════════════════
        static Panel MkPanel(DockStyle dock, int h, Color bg)
        { Panel p = new Panel(); p.Dock = dock; p.Height = h; p.BackColor = bg; return p; }

        static Label MkLbl(string txt, Font f, Color fc)
        { Label l = new Label(); l.Text = txt; l.Font = f; l.ForeColor = fc; l.BackColor = Color.Transparent; return l; }

        static Button MkBtn(string txt, Color bg, int w, int h)
        {
            Button b = new Button();
            b.Text = txt;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.BorderSize = 0;
            if (w > 0 && h > 0) b.Size = new Size(w, h);
            return b;
        }

        static void HLine(PaintEventArgs e, int w, Color c, int y)
        { Pen p = new Pen(c); e.Graphics.DrawLine(p, 0, y, w, y); p.Dispose(); }

        static void RoundRect(Graphics g, Pen pen, Rectangle r, int rad)
        {
            GraphicsPath p = RoundPath(r, rad); g.DrawPath(pen, p); p.Dispose();
        }

        static void FillPill(Graphics g, Color c, Rectangle r)
        {
            GraphicsPath p = RoundPath(r, r.Height / 2);
            SolidBrush br = new SolidBrush(c); g.FillPath(br, p); br.Dispose(); p.Dispose();
        }

        static void DrawPill(Graphics g, Pen pen, Rectangle r)
        {
            GraphicsPath p = RoundPath(r, r.Height / 2); g.DrawPath(pen, p); p.Dispose();
        }

        static GraphicsPath RoundPath(Rectangle r, int rad)
        {
            int d = rad * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════
        // Pre-compute a darkened version of a bitmap (CB overlay at 70% baked-in)
        // Uses Graphics.FromImage → correct alpha compositing, done only once.
        static Bitmap BakeDarken(Bitmap src)
        {
            Bitmap result = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(src, 0, 0, src.Width, src.Height);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(178, CB.R, CB.G, CB.B)))
                    g.FillRectangle(br, 0, 0, src.Width, src.Height);
            }
            return result;
        }

        // Load embedded .ico (Build Action = Embedded Resource in VS)
        static Icon LoadEmbeddedIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string rn in asm.GetManifestResourceNames())
                    if (rn.ToLowerInvariant().EndsWith(".ico"))
                        using (Stream st = asm.GetManifestResourceStream(rn))
                            return new Icon(st);
            }
            catch { }
            return null;
        }

        // ── Auto-detect Constance in Steam ────────────────────────────────────
        // Reads Windows registry to find Steam, parses libraryfolders.vdf
        // to cover all libraries, looks for "Constance" folder
        // and returns the first .exe found. Returns null if nothing found.
        static string TryFindConstanceSteam()
        {
            try
            {
                // 1. Steam path from registry
                string steamPath = null;
                foreach (string regKey in new[] {
                    @"HKEY_CURRENT_USER\Software\Valve\Steam",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam" })
                {
                    object val = Microsoft.Win32.Registry.GetValue(regKey, "SteamPath", null)
                              ?? Microsoft.Win32.Registry.GetValue(regKey, "InstallPath", null);
                    if (val != null) { steamPath = val.ToString().Replace('/', '\\'); break; }
                }
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                    return null;

                // 2. All Steam libraries from libraryfolders.vdf
                var libraries = new List<string>();
                string defaultLib = Path.Combine(steamPath, "steamapps");
                if (Directory.Exists(defaultLib)) libraries.Add(defaultLib);

                string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (string line in File.ReadAllLines(vdf))
                    {
                        // Typical line: "path"   "D:\\SteamLibrary"
                        string t = line.Trim();
                        if (!t.Contains("\"path\"")) continue;
                        int q1 = t.LastIndexOf('"', t.Length - 1);
                        if (q1 < 0) continue;
                        int q0 = t.LastIndexOf('"', q1 - 1);
                        if (q0 < 0) continue;
                        string libPath = t.Substring(q0 + 1, q1 - q0 - 1)
                                          .Replace("\\\\", "\\");
                        string sa = Path.Combine(libPath, "steamapps");
                        if (Directory.Exists(sa) && !libraries.Contains(sa))
                            libraries.Add(sa);
                    }
                }

                // 3. Look for a "Constance" folder in steamapps/common
                foreach (string lib in libraries)
                {
                    string common = Path.Combine(lib, "common");
                    if (!Directory.Exists(common)) continue;
                    foreach (string dir in Directory.GetDirectories(common))
                    {
                        string name = Path.GetFileName(dir).ToLowerInvariant();
                        if (!name.Contains("constance")) continue;

                        // Look for exe: prefer one containing "constance"
                        string[] exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                        foreach (string exe in exes)
                            if (Path.GetFileNameWithoutExtension(exe).ToLowerInvariant().Contains("constance"))
                                return exe;
                        if (exes.Length > 0) return exes[0];
                    }
                }
            }
            catch { }
            return null;
        }

        static string NiceName(string r)
        {
            return r.Replace("_", " ").Replace("BossRush", "Boss Rush")
                    .Replace("CorruptedSkins", "Corrupted Skins").Trim();
        }

        static string DllVer(string path)
        {
            try
            {
                System.Version v = AssemblyName.GetAssemblyName(path).Version;
                return v != null ? v.Major + "." + v.Minor : "?";
            }
            catch { return "?"; }
        }
    }
}
