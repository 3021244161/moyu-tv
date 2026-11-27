using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MoyuTv
{
    internal sealed class Television : Form
    {
        private static readonly Color Shell = Color.FromArgb(16, 27, 38);
        private static readonly Color Surface = Color.FromArgb(24, 40, 52);
        private static readonly Color Ink = Color.FromArgb(232, 241, 242);
        private static readonly Color Quiet = Color.FromArgb(168, 188, 197);
        private static readonly Color Mint = Color.FromArgb(103, 223, 195);
        private static readonly double[] ZoomLevels = { 0.5, 0.67, 0.8, 1.0, 1.25, 1.5 };
        private readonly WindowSettings settings = new WindowSettings();
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip options = new ContextMenuStrip();
        private readonly Label location = new Label();
        private readonly StationHome stations = new StationHome();
        private readonly TvBrowser browser = new TvBrowser();
        private readonly Panel bar = new Panel();
        private readonly Label corner = new Label();
        private readonly Timer hover = new Timer { Interval = 80 };
        private readonly Button channel, back, play, sound, reader, fill, pin, more, close;
        private readonly Font icons = new Font("Segoe MDL2 Assets", 12);
        private readonly Uri start;
        private readonly WindowWheelZoom wheelZoom;
        private bool onHome = true, hasPage, muted;
        private bool screenFullscreen, windowPinned;
        private double zoom;
        private DateTime keepToolsUntil;
        private bool dragging, resizing, keyboardTools;
        private Size resizeStart;
        private Point pointerStart, windowStart, keyboardPointer;
        private Rectangle windowedFrame;
        private const int DefaultWidth = 504, DefaultHeight = 353;

        public Television(Uri startPage)
        {
            SuspendLayout();
            start = startPage;
            Text = "摸鱼电视机";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Microsoft YaHei UI", 9);
            BackColor = Shell;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(DefaultWidth, DefaultHeight);
            MinimumSize = new Size(351, 252);
            StartPosition = FormStartPosition.Manual;

            Panel screen = new Panel { Dock = DockStyle.Fill, BackColor = Shell };
            stations.Dock = browser.Dock = DockStyle.Fill;
            browser.Visible = false;
            screen.Controls.Add(browser);
            screen.Controls.Add(stations);
            Controls.Add(screen);

            bar.Bounds = new Rectangle(0, 0, ClientSize.Width, 36);
            bar.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            bar.BackColor = Surface;
            bar.Padding = new Padding(4, 1, 4, 1);
            bar.AccessibleName = "遥控条";
            bar.Visible = false;
            Controls.Add(bar);
            bar.BringToFront();
            FlowLayoutPanel left = ButtonGroup(DockStyle.Left);
            FlowLayoutPanel right = ButtonGroup(DockStyle.Right);
            channel = AddButton(left, "\uE80F", "选择频道", 28, true);
            channel.Click += delegate { ToggleHome(); };
            back = AddButton(left, "\uE72B", "返回上一页", 27, true);
            back.Click += delegate { browser.GoBack(); };
            play = AddButton(right, "\uE768\uE769", "播放 / 暂停", 38, true);
            play.Click += delegate { browser.TogglePlay(); };
            sound = AddButton(right, "\uE767", "静音", 27, true);
            sound.Click += delegate { muted = !muted; browser.SetMuted(muted); RefreshBar(); };
            reader = AddButton(right, "\uE736", "阅读模式", 29, true);
            reader.Click += async delegate
            {
                if (!await browser.ToggleReaderAsync() && !IsDisposed && Visible)
                    tips.Show("请先打开小说章节或文章正文。", this, 8, bar.Height + 4, 4200);
                RefreshBar();
            };
            fill = AddButton(right, "\uE740", "全屏展示 (F11)", 28, true);
            fill.Click += delegate { ToggleScreenFullscreen(); };
            pin = AddButton(right, "", "切换窗口置顶", 52, false);
            pin.Click += delegate { TopMost = !TopMost; RefreshBar(); };
            more = AddButton(right, "\uE712", "更多选项", 28, true);
            more.Click += delegate { OpenOptions(); };
            close = AddButton(right, "\uE8BB", "关闭电视", 29, true);
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(154, 54, 59);
            close.Click += delegate { Close(); };
            location.Dock = DockStyle.Fill;
            location.AutoEllipsis = true;
            location.ForeColor = Quiet;
            location.Padding = new Padding(6, 0, 3, 0);
            location.TextAlign = ContentAlignment.MiddleLeft;
            location.AccessibleName = "拖动窗口";
            location.Cursor = Cursors.SizeAll;
            location.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) DragWindow(); };
            bar.Controls.Add(location);
            bar.Controls.Add(left);
            bar.Controls.Add(right);
            corner.Bounds = new Rectangle(ClientSize.Width - 22, ClientSize.Height - 22, 22, 22);
            corner.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            corner.Text = "\uE784";
            corner.TextAlign = ContentAlignment.MiddleCenter;
            corner.Font = icons;
            corner.BackColor = Surface;
            corner.ForeColor = Mint;
            corner.Cursor = Cursors.SizeNWSE;
            corner.AccessibleName = "拖动右下角调整大小";
            tips.SetToolTip(corner, corner.AccessibleName);
            corner.Visible = false;
            Controls.Add(corner);
            corner.BringToFront();
            corner.MouseDown += StartResize;
            corner.MouseMove += ContinueResize;
            corner.MouseUp += delegate { resizing = false; corner.Capture = false; };
            corner.MouseCaptureChanged += delegate { if (!corner.Capture) resizing = false; };
            hover.Tick += delegate { UpdateOverlay(); };

            TopMost = settings.Pinned;
            zoom = Array.IndexOf(ZoomLevels, settings.Zoom) >= 0 ? settings.Zoom : 1.0;
            if (settings.LayoutVersion < 2 && zoom == 0.8) zoom = 1.0;
            muted = settings.Muted;
            browser.AutoFillVideo = settings.AutoFill;
            browser.ReaderTextSize = Math.Max(16, Math.Min(32, settings.ReaderTextSize));
            browser.SetZoom(zoom);
            browser.SetMuted(muted);
            browser.Changed += delegate { RefreshBar(); };
            browser.FullscreenChanged += delegate { RefreshBar(); };
            browser.PlayerKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (HandleScreenKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
            };
            browser.Notice += delegate(string message)
            {
                if (Visible && !Disposing) tips.Show(message, this, 8, bar.Height + 4, 4200);
            };
            stations.Tune += Tune;
            stations.ReadRequested += delegate(Uri address) { OpenPage(address, true); };
            stations.DragRequested += delegate { DragWindow(); };
            wheelZoom = new WindowWheelZoom(this, delegate { return screenFullscreen; }, RestoreWindow);
            RefreshBar();
            ResumeLayout(false);
        }

        private static FlowLayoutPanel ButtonGroup(DockStyle side)
        {
            return new FlowLayoutPanel { Dock = side, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        }

        private Button AddButton(FlowLayoutPanel group, string text, string hint, int width, bool symbol)
        {
            Button item = new Button { Text = text, AccessibleName = hint, Width = width, Height = 32,
                FlatStyle = FlatStyle.Flat, ForeColor = Ink, BackColor = Surface, Margin = new Padding(1, 1, 1, 0),
                Cursor = Cursors.Hand, Font = symbol ? icons : Font, UseVisualStyleBackColor = false };
            item.FlatAppearance.BorderSize = 0;
            item.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 64, 75);
            item.FlatAppearance.MouseDownBackColor = Color.FromArgb(52, 78, 86);
            group.Controls.Add(item);
            tips.SetToolTip(item, hint);
            return item;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Rectangle frame = settings.Frame;
            if (frame.Width > 0 && frame.Height > 0)
            {
                Screen savedScreen = Screen.FromRectangle(frame);
                area = savedScreen.WorkingArea;
                if (settings.LayoutVersion < 1)
                    frame.Size = new Size((int)Math.Round(frame.Width * 0.9), (int)Math.Round(frame.Height * 0.9));
                if (settings.LayoutVersion < 2)
                {
                    // Earlier builds saved the DPI-virtualized desktop coordinates.
                    using (Graphics g = CreateGraphics())
                    {
                        Rectangle bounds = savedScreen.Bounds;
                        frame = new Rectangle(bounds.Left + (int)Math.Round((frame.Left - bounds.Left) * g.DpiX / 96),
                            bounds.Top + (int)Math.Round((frame.Top - bounds.Top) * g.DpiY / 96),
                            (int)Math.Round(frame.Width * g.DpiX / 96), (int)Math.Round(frame.Height * g.DpiY / 96));
                    }
                }
            }
            else
                frame = new Rectangle(area.Right - Width - 18, area.Bottom - Height - 18, Width, Height);
            int w = Math.Min(area.Width, Math.Max(MinimumSize.Width, frame.Width));
            int h = Math.Min(area.Height, Math.Max(MinimumSize.Height, frame.Height));
            Bounds = new Rectangle(Math.Max(area.Left, Math.Min(frame.X, area.Right - w)),
                Math.Max(area.Top, Math.Min(frame.Y, area.Bottom - h)), w, h);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            hover.Start();
            if (start != null) Tune(start);
        }

        private void UpdateOverlay()
        {
            if (WindowState == FormWindowState.Minimized || !Visible) return;
            Point pointer = Cursor.Position;
            if (keyboardTools && keyboardPointer != pointer) keyboardTools = false;
            Point local = PointToClient(pointer);
            bool inside = ClientRectangle.Contains(local);
            if (inside && (local.Y < Math.Max(5, bar.Height / 6) || (bar.Visible && bar.Bounds.Contains(local))))
                keepToolsUntil = DateTime.UtcNow.AddMilliseconds(500);
            bool show = options.Visible || (keyboardTools && bar.ContainsFocus) || DateTime.UtcNow < keepToolsUntil;
            if (bar.Visible != show)
            {
                bar.Visible = show;
                if (show) bar.BringToFront();
            }
            corner.Visible = !screenFullscreen && (resizing || (inside && local.X >= ClientSize.Width - corner.Width && local.Y >= ClientSize.Height - corner.Height));
        }

        private void DragWindow()
        {
            if (screenFullscreen) return;
            pointerStart = Cursor.Position;
            windowStart = Location;
            dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point at = Cursor.Position;
            Location = new Point(windowStart.X + at.X - pointerStart.X, windowStart.Y + at.Y - pointerStart.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging && e.Button == MouseButtons.Left) { dragging = false; Capture = false; }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) dragging = false;
        }

        private void StartResize(object sender, MouseEventArgs e)
        {
            if (screenFullscreen || e.Button != MouseButtons.Left) return;
            resizing = true;
            resizeStart = Size;
            pointerStart = Cursor.Position;
            corner.Capture = true;
        }

        private void ContinueResize(object sender, MouseEventArgs e)
        {
            if (!resizing) return;
            Point at = Cursor.Position;
            Size = new Size(Math.Max(MinimumSize.Width, resizeStart.Width + at.X - pointerStart.X),
                Math.Max(MinimumSize.Height, resizeStart.Height + at.Y - pointerStart.Y));
        }

        private void Tune(Uri address)
        {
            OpenPage(address, false);
        }

        private void OpenPage(Uri address, bool reading)
        {
            browser.PreferReading = reading;
            hasPage = true;
            ShowHome(false);
            browser.Open(address);
        }

        private async void ToggleScreenFullscreen()
        {
            if (screenFullscreen) { RestoreWindow(); return; }
            windowedFrame = Bounds;
            windowPinned = TopMost;
            screenFullscreen = true;
            TopMost = false;
            Bounds = Screen.FromRectangle(windowedFrame).Bounds;
            Activate();
            RefreshBar();
            if (!onHome && browser.IsReady && !browser.IsContentFullscreen && !browser.IsReading && !browser.PreferReading)
            {
                string info = await browser.GetVideoQualityInfoAsync();
                if (screenFullscreen && !IsDisposed && !onHome && !browser.PreferReading && !browser.IsReading
                    && !String.IsNullOrEmpty(info) && !browser.IsContentFullscreen)
                    browser.ToggleVideoFullscreen();
            }
        }

        private void RestoreWindow()
        {
            if (!screenFullscreen) return;
            screenFullscreen = false;
            Rectangle area = Screen.FromRectangle(windowedFrame).WorkingArea;
            int w = Math.Min(windowedFrame.Width, area.Width), h = Math.Min(windowedFrame.Height, area.Height);
            Bounds = new Rectangle(Math.Max(area.Left, Math.Min(windowedFrame.Left, area.Right - w)),
                Math.Max(area.Top, Math.Min(windowedFrame.Top, area.Bottom - h)), w, h);
            TopMost = windowPinned;
            RefreshBar();
        }

        private bool HandleScreenKey(Keys keys)
        {
            if (keys == Keys.F11) { ToggleScreenFullscreen(); return true; }
            if (screenFullscreen && keys == Keys.Escape) { RestoreWindow(); return true; }
            return false;
        }

        private void ToggleHome()
        {
            if (!onHome || hasPage) ShowHome(!onHome);
        }

        private void ShowHome(bool value)
        {
            onHome = value;
            // Keep playback alive while choosing another station.
            if (onHome) { stations.Visible = true; stations.BringToFront(); }
            else { browser.Visible = true; browser.BringToFront(); stations.Visible = false; }
            RefreshBar();
        }

        private void RefreshBar()
        {
            if (IsDisposed || Disposing) return;
            Uri url = browser.CurrentUrl;
            string host = url == null ? "" : url.Authority;
            bool showPlayer = hasPage && browser.IsReady;
            channel.Visible = hasPage;
            channel.Text = onHome ? "\uE714" : "\uE80F";
            channel.AccessibleName = onHome ? "返回当前频道" : "选择频道";
            tips.SetToolTip(channel, channel.AccessibleName);
            back.Visible = !onHome;
            back.Enabled = browser.CanGoBack;
            play.Visible = sound.Visible = hasPage && !browser.IsReading && !browser.PreferReading;
            play.Enabled = sound.Enabled = showPlayer;
            reader.Visible = !onHome;
            reader.Enabled = showPlayer;
            reader.AccessibleName = browser.IsReading ? "退出阅读模式，返回原网页" : "阅读模式";
            reader.ForeColor = browser.IsReading ? Mint : Ink;
            tips.SetToolTip(reader, reader.AccessibleName);
            fill.Text = screenFullscreen ? "\uE73F" : "\uE740";
            fill.AccessibleName = screenFullscreen ? "退出显示器全屏 (Esc)" : "全屏展示 (F11)";
            tips.SetToolTip(fill, fill.AccessibleName);
            sound.Text = muted ? "\uE74F" : "\uE767";
            sound.AccessibleName = muted ? "取消静音" : "静音";
            tips.SetToolTip(sound, sound.AccessibleName);
            pin.Text = TopMost ? "已置顶" : "置顶";
            pin.ForeColor = TopMost ? Mint : Ink;
            pin.Visible = !screenFullscreen;
            pin.AccessibleName = TopMost ? "取消窗口置顶" : "窗口置顶";
            tips.SetToolTip(pin, pin.AccessibleName);
            string label = onHome ? (screenFullscreen ? "全屏展示" : "拖动窗口") :
                (host.Length == 0 ? "正在打开频道…" : host);
            if (location.Text != label) location.Text = label;
            bool showOrigin = !onHome && url != null;
            location.Cursor = screenFullscreen ? Cursors.Default : Cursors.SizeAll;
            tips.SetToolTip(location, showOrigin ? (screenFullscreen ? "" : "拖动窗口  ·  ") + url.AbsoluteUri : label);
            Text = onHome || String.IsNullOrWhiteSpace(browser.PageTitle) ? "摸鱼电视机" : "摸鱼电视机 · " + browser.PageTitle;
        }

        private void OpenOptions()
        {
            while (options.Items.Count > 0) options.Items[0].Dispose();
            options.Items.Add("打开链接…", null, delegate { ShowHome(true); stations.FocusAddress(); });
            ToolStripMenuItem reload = new ToolStripMenuItem("重新加载网页");
            reload.Enabled = hasPage;
            reload.Click += delegate { ShowHome(false); browser.Reload(); };
            options.Items.Add(reload);
            ToolStripMenuItem fonts = new ToolStripMenuItem("阅读字号");
            foreach (int size in new[] { 16, 18, 20, 24, 28, 32 })
            {
                int pixels = size;
                ToolStripMenuItem entry = new ToolStripMenuItem(pixels + " px") { Checked = browser.ReaderTextSize == pixels };
                entry.Click += delegate { browser.ReaderTextSize = pixels; };
                fonts.DropDownItems.Add(entry);
            }
            options.Items.Add(fonts);
            if (!browser.IsReading && !browser.PreferReading)
            {
                ToolStripMenuItem quality = new ToolStripMenuItem("视频画质…") { Enabled = hasPage && browser.IsReady };
                quality.Click += async delegate
                {
                    ShowHome(false);
                    if (!await browser.OpenQualityMenuAsync() && !IsDisposed && Visible)
                        tips.Show("请在网页播放器的「设置」或「清晰度」中选择画质。", this, 8, bar.Height + 4, 5500);
                };
                options.Items.Add(quality);
                if (quality.Enabled) UpdateQualityLabel(quality);
                ToolStripMenuItem fit = new ToolStripMenuItem(browser.IsContentFullscreen ? "返回原网页" : "视频铺满小窗");
                fit.Enabled = hasPage && browser.IsReady;
                fit.Click += delegate { ShowHome(false); browser.ToggleVideoFullscreen(); };
                options.Items.Add(fit);
                ToolStripMenuItem autoFill = new ToolStripMenuItem("播放时自动铺满") { Checked = browser.AutoFillVideo };
                autoFill.Click += delegate { browser.AutoFillVideo = !browser.AutoFillVideo; };
                options.Items.Add(autoFill);
            }
            options.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem sizes = new ToolStripMenuItem("网页缩放");
            foreach (double level in ZoomLevels)
            {
                double factor = level;
                ToolStripMenuItem entry = new ToolStripMenuItem(Math.Round(level * 100) + "%");
                entry.Checked = Math.Abs(level - zoom) < 0.001;
                entry.Click += delegate { zoom = factor; browser.SetZoom(zoom); };
                sizes.DropDownItems.Add(entry);
            }
            options.Items.Add(sizes);
            options.Items.Add("恢复默认大小", null, delegate { RestoreWindow(); Size = LogicalDefaultSize(); });
            options.Items.Add("最小化", null, delegate { WindowState = FormWindowState.Minimized; });
            Uri target = browser.CurrentUrl;
            ToolStripMenuItem external = new ToolStripMenuItem("在默认浏览器打开当前页");
            external.Enabled = target != null && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps);
            external.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { tips.Show("无法打开默认浏览器。", more, 0, more.Height, 3000); }
            };
            options.Items.Add(external);
            options.Show(more, new Point(more.Width, more.Height), ToolStripDropDownDirection.BelowLeft);
        }

        private async void UpdateQualityLabel(ToolStripMenuItem item)
        {
            string size = await browser.GetVideoQualityInfoAsync();
            if (!item.IsDisposed && !String.IsNullOrEmpty(size)) item.Text = "视频画质  ·  " + size + "…";
        }

        private Size LogicalDefaultSize()
        {
            using (Graphics g = CreateGraphics())
                return new Size((int)Math.Round(DefaultWidth * g.DpiX / 96), (int)Math.Round(DefaultHeight * g.DpiY / 96));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleScreenKey(keyData)) return true;
            if (keyData == Keys.F6)
            {
                keyboardTools = true;
                keyboardPointer = Cursor.Position;
                keepToolsUntil = DateTime.UtcNow.AddSeconds(1);
                bar.Show();
                bar.BringToFront();
                pin.Focus();
                return true;
            }
            if (keyData == Keys.Escape && keyboardTools && bar.ContainsFocus)
            {
                keyboardTools = false;
                keepToolsUntil = DateTime.MinValue;
                bar.Hide();
                if (onHome) stations.FocusAddress(); else browser.Focus();
                return true;
            }
            if (keyData == (Keys.Control | Keys.L))
            {
                ShowHome(true);
                stations.FocusAddress();
                return true;
            }
            if (onHome && hasPage && keyData == Keys.Escape)
            {
                ShowHome(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            settings.Frame = screenFullscreen ? windowedFrame : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.LayoutVersion = 2;
            settings.Pinned = screenFullscreen ? windowPinned : TopMost;
            settings.Zoom = zoom;
            settings.AutoFill = browser.AutoFillVideo;
            settings.ReaderTextSize = browser.ReaderTextSize;
            settings.Muted = muted;
            try { settings.Save(); }
            catch (ConfigurationErrorsException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (wheelZoom != null) wheelZoom.Dispose(); hover.Dispose(); options.Dispose(); tips.Dispose(); icons.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class WindowSettings : ApplicationSettingsBase
    {
        [UserScopedSetting, DefaultSettingValue("0, 0, 0, 0")]
        public Rectangle Frame { get { return (Rectangle)this["Frame"]; } set { this["Frame"] = value; } }
        [UserScopedSetting, DefaultSettingValue("True")]
        public bool Pinned { get { return (bool)this["Pinned"]; } set { this["Pinned"] = value; } }
        [UserScopedSetting, DefaultSettingValue("1.0")]
        public double Zoom { get { return (double)this["Zoom"]; } set { this["Zoom"] = value; } }
        [UserScopedSetting, DefaultSettingValue("True")]
        public bool AutoFill { get { return (bool)this["AutoFill"]; } set { this["AutoFill"] = value; } }
        [UserScopedSetting, DefaultSettingValue("20")]
        public int ReaderTextSize { get { return (int)this["ReaderTextSize"]; } set { this["ReaderTextSize"] = value; } }
        [UserScopedSetting, DefaultSettingValue("0")]
        public int LayoutVersion { get { return (int)this["LayoutVersion"]; } set { this["LayoutVersion"] = value; } }
        [UserScopedSetting, DefaultSettingValue("False")]
        public bool Muted { get { return (bool)this["Muted"]; } set { this["Muted"] = value; } }
    }
}
