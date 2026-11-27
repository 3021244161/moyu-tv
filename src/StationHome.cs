using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MoyuTv
{
    public sealed class StationHome : UserControl
    {
        private static readonly Color Ink = Color.FromArgb(16, 27, 38);
        private static readonly Color Surface = Color.FromArgb(26, 42, 54);
        private static readonly Color Line = Color.FromArgb(58, 80, 90);
        private static readonly Color Mint = Color.FromArgb(103, 223, 195);
        private static readonly Color Paper = Color.FromArgb(238, 244, 240);
        private static readonly Color Error = Color.FromArgb(255, 167, 142);
        private readonly string[] names = { "YouTube", "哔哩哔哩", "爱奇艺", "腾讯视频", "酷狗音乐", "小说", "文章" };
        private readonly string[] compactNames = { "YT", "B站", "爱奇艺", "腾讯", "酷狗", "小说", "文章" };
        private readonly string[] sites = {
            "https://www.youtube.com/", "https://www.bilibili.com/", "https://www.iqiyi.com/",
            "https://v.qq.com/", "https://www.kugou.com/"
        };
        private readonly ChannelKey[] channels = new ChannelKey[7];
        private readonly RadioButton mediaTab = new RadioButton();
        private readonly RadioButton readTab = new RadioButton();
        private readonly TextBox address = new TextBox();
        private readonly Label placeholder = new Label();
        private readonly Button open = new Button();
        private readonly ToolTip hint = new ToolTip();
        private readonly ContextMenuStrip musicMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip novelMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip articleMenu = new ContextMenuStrip();
        private readonly Font headingFont = new Font("Microsoft YaHei UI", 18, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 9);
        private readonly Font keyFont = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        private readonly Font compactKeyFont = new Font("Microsoft YaHei UI", 8, FontStyle.Bold);
        private readonly Font logoFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        private Rectangle screen;
        private Rectangle inputBox;
        private bool invalidAddress;
        private float dpiScale = 1;

        public event Action<Uri> Tune;
        public event Action<Uri> ReadRequested;
        public event EventHandler DragRequested;

        public StationHome()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ink;
            ForeColor = Paper;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            AccessibleName = "频道选择";

            musicMenu.AccessibleName = "酷狗音乐频道";
            novelMenu.AccessibleName = "小说阅读入口";
            articleMenu.AccessibleName = "文章阅读入口";
            foreach (ContextMenuStrip menu in new[] { musicMenu, novelMenu, articleMenu })
            {
                menu.Font = bodyFont;
                menu.ShowImageMargin = false;
            }
            musicMenu.Items.Add("酷狗 5sing 音乐馆", null, delegate {
                SelectStation(new Uri("https://5sing.kugou.com/"));
            });
            musicMenu.Items.Add("酷狗音乐官网", null, delegate { SelectStation(new Uri(sites[4])); });
            musicMenu.Items.Add("试听《小星星》伴奏", null, delegate {
                SelectStation(new Uri("https://5sing.kugou.com/m/Song/Detail/bz/2397808.html"));
            });
            AddReadingEntry(novelMenu, "读《三国演义》第一回", "https://zh.wikisource.org/zh-hans/三國演義/第001回");
            AddReadingEntry(novelMenu, "《三国演义》完整目录", "https://zh.wikisource.org/zh-hans/三國演義");
            AddReadingEntry(novelMenu, "逛起点中文网", "https://www.qidian.com/");
            AddReadingEntry(articleMenu, "读《在少数派写稿两年后，我养成的数字生活方式》", "https://sspai.com/post/42714");
            AddReadingEntry(articleMenu, "浏览少数派文章", "https://sspai.com/");
            ConfigureTab(mediaTab, "影音", 0);
            ConfigureTab(readTab, "小说 · 文章", 1);

            for (int i = 0; i < channels.Length; ++i)
            {
                int station = i;
                ChannelKey button = new ChannelKey(i, logoFont);
                channels[i] = button;
                button.Text = names[i];
                button.AccessibleName = names[i];
                button.Font = keyFont;
                button.TabIndex = i + 2;
                if (station >= 4)
                {
                    ContextMenuStrip menu = station == 4 ? musicMenu : station == 5 ? novelMenu : articleMenu;
                    button.AccessibleRole = AccessibleRole.ButtonDropDown;
                    button.AccessibleDescription = menu.AccessibleName;
                    button.Click += delegate { ShowMenu(menu, button); };
                }
                else button.Click += delegate { SelectStation(new Uri(sites[station])); };
                Controls.Add(button);
            }

            address.Font = bodyFont;
            address.BackColor = Surface;
            address.ForeColor = Paper;
            address.BorderStyle = BorderStyle.None;
            address.AccessibleName = "视频、歌曲、小说或文章链接";
            address.TabIndex = 9;
            address.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                OpenAddress();
            };
            address.GotFocus += delegate { UpdatePlaceholder(); };
            address.LostFocus += delegate { UpdatePlaceholder(); };
            address.TextChanged += delegate { invalidAddress = false; hint.Hide(address); UpdatePlaceholder(); };
            Controls.Add(address);
            placeholder.Text = "粘贴视频、歌曲、小说或文章链接";
            placeholder.Font = bodyFont;
            placeholder.ForeColor = Color.FromArgb(173, 194, 201);
            placeholder.BackColor = Surface;
            placeholder.AutoSize = false;
            placeholder.TextAlign = ContentAlignment.MiddleLeft;
            placeholder.Cursor = Cursors.IBeam;
            placeholder.Click += delegate { address.Focus(); };
            Controls.Add(placeholder);
            placeholder.BringToFront();

            open.Text = "打开";
            open.AccessibleName = "打开链接";
            open.Font = keyFont;
            open.FlatStyle = FlatStyle.Flat;
            open.FlatAppearance.BorderSize = 0;
            open.FlatAppearance.MouseOverBackColor = Color.FromArgb(139, 239, 214);
            open.BackColor = Mint;
            open.ForeColor = Ink;
            open.Cursor = Cursors.Hand;
            open.TabIndex = 10;
            open.Click += delegate { OpenAddress(); };
            Controls.Add(open);
            mediaTab.Checked = true;
            readTab.CheckedChanged += delegate { UpdateCategory(); };
            UpdateCategory();
        }

        private void ConfigureTab(RadioButton tab, string text, int tabIndex)
        {
            tab.Appearance = Appearance.Button;
            tab.AutoSize = false;
            tab.FlatStyle = FlatStyle.Flat;
            tab.FlatAppearance.BorderColor = Line;
            tab.FlatAppearance.CheckedBackColor = Mint;
            tab.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 86, 89);
            tab.BackColor = Surface;
            tab.Cursor = Cursors.Hand;
            tab.Font = keyFont;
            tab.Text = text;
            tab.AccessibleName = text;
            tab.TextAlign = ContentAlignment.MiddleCenter;
            tab.TabIndex = tabIndex;
            Controls.Add(tab);
        }

        private void UpdateCategory()
        {
            bool reading = readTab.Checked;
            mediaTab.ForeColor = reading ? Paper : Ink;
            readTab.ForeColor = reading ? Ink : Paper;
            mediaTab.FlatAppearance.MouseOverBackColor = reading ? Line : Mint;
            readTab.FlatAppearance.MouseOverBackColor = reading ? Mint : Line;
            for (int i = 0; i < channels.Length; ++i)
                channels[i].Visible = (i >= sites.Length) == reading;
            open.Text = reading ? "阅读" : "打开";
            open.AccessibleName = reading ? "用阅读模式打开链接" : "打开链接";
            PerformLayout();
            Invalidate();
        }

        private void AddReadingEntry(ContextMenuStrip menu, string title, string location)
        {
            menu.Items.Add(title, null, delegate { SelectStation(new Uri(location), true); });
        }

        private void UpdatePlaceholder()
        {
            placeholder.Visible = !address.Focused && address.TextLength == 0;
            PerformLayout();
            Invalidate(inputBox);
        }

        private static void ShowMenu(ContextMenuStrip menu, Control anchor)
        {
            Rectangle key = anchor.RectangleToScreen(anchor.ClientRectangle);
            Rectangle desktop = Screen.FromControl(anchor).WorkingArea;
            Size preferred = menu.GetPreferredSize(Size.Empty);
            bool above = desktop.Bottom - key.Bottom < preferred.Height;
            bool alignRight = key.Left + preferred.Width > desktop.Right;
            menu.Show(anchor, new Point(alignRight ? anchor.Width : 0, above ? 0 : anchor.Height),
                alignRight ? (above ? ToolStripDropDownDirection.AboveLeft : ToolStripDropDownDirection.BelowLeft)
                           : (above ? ToolStripDropDownDirection.AboveRight : ToolStripDropDownDirection.BelowRight));
            menu.Items[0].Select();
        }

        public void FocusAddress()
        {
            address.Focus();
            address.SelectAll();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (inputBox.Contains(e.Location))
            {
                address.Focus();
                return;
            }
            EventHandler drag = DragRequested;
            if (drag != null) drag(this, EventArgs.Empty);
        }

        private void OpenAddress()
        {
            Uri url;
            if (Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out url) &&
                (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrEmpty(url.Host))
            {
                SelectStation(url, readTab.Checked);
                return;
            }
            invalidAddress = true;
            FocusAddress();
            hint.Show("请输入以 http:// 或 https:// 开头的完整链接", address, 0, -Px(28), 4500);
            Invalidate();
        }

        private void SelectStation(Uri url, bool reading = false)
        {
            invalidAddress = false;
            hint.Hide(address);
            Invalidate(inputBox);
            Action<Uri> handler = reading ? ReadRequested : Tune;
            if (handler != null) handler(url);
        }

        private int Px(int value) { return (int)Math.Round(value * dpiScale); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateDpi();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            UpdateDpi();
        }

        private void UpdateDpi()
        {
            dpiScale = DeviceDpi / 96f;
            PerformLayout();
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (open == null || open.Parent == null) return;
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            bool compact = width < Px(330);
            placeholder.Text = compact ? "粘贴影音或阅读链接" : "粘贴视频、歌曲、小说或文章链接";
            int pad = width < Px(450) || height < Px(240) ? Px(12) : Px(20);
            int gap = height < Px(290) ? Px(7) : Px(10);
            int available = Math.Max(1, width - 2 * pad);
            int inputH = Math.Max(Px(33), address.PreferredHeight + Px(13));
            int tabH = Math.Max(Px(27), keyFont.Height + Px(8));
            int cardH = Math.Min(Px(82), Math.Max(Px(48), (height - 2 * pad - inputH - tabH) / 3));
            int contentH = Math.Min(Px(396), height - 2 * pad);
            int y = Math.Max(pad, (height - contentH) / 2);
            int heroH = Math.Max(0, contentH - cardH - inputH - tabH - 3 * gap);
            screen = new Rectangle(pad, y, available, heroH);
            int keyGap = width < Px(450) ? Px(4) : Px(7);
            int tabY = screen.Bottom + gap;
            mediaTab.SetBounds(pad, tabY, Px(66), tabH);
            readTab.SetBounds(mediaTab.Right + keyGap, tabY, Px(102), tabH);
            int first = readTab.Checked ? sites.Length : 0;
            int count = readTab.Checked ? channels.Length - sites.Length : sites.Length;
            int keySpace = Math.Max(count, available - (count - 1) * keyGap);
            int keyY = tabY + tabH + gap;
            for (int i = 0; i < count; i++)
            {
                int left = keySpace * i / count;
                int right = keySpace * (i + 1) / count;
                ChannelKey key = channels[first + i];
                key.Font = compact ? compactKeyFont : keyFont;
                string label = (compact ? compactNames : names)[first + i];
                if (key.Text != label) key.Text = label;
                key.SetBounds(pad + left + i * keyGap, keyY, right - left, cardH);
            }
            int inputY = keyY + cardH + gap;
            int openWidth = Px(64);
            inputBox = new Rectangle(pad, inputY, Math.Max(1, available - keyGap - openWidth), inputH);
            int textTop = inputY + (inputH - address.PreferredHeight) / 2;
            Rectangle edit = new Rectangle(inputBox.Left + Px(10), textTop,
                Math.Max(1, inputBox.Width - Px(20)), address.PreferredHeight);
            placeholder.Bounds = edit;
            address.Bounds = !address.Focused && address.TextLength == 0
                ? new Rectangle(edit.Right, edit.Top, 1, edit.Height) : edit;
            open.SetBounds(inputBox.Right + keyGap, inputY, openWidth, inputH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            if (screen.Width > 0 && screen.Height > 1) PaintScreen(g);
            if (inputBox.Width < 1 || inputBox.Height < 1) return;
            using (SolidBrush fill = new SolidBrush(Surface)) g.FillRectangle(fill, inputBox);
            using (Pen edge = new Pen(invalidAddress ? Error : address.Focused ? Mint : Line))
                g.DrawRectangle(edge, inputBox.X, inputBox.Y, inputBox.Width - 1, inputBox.Height - 1);
        }

        private void PaintScreen(Graphics g)
        {
            GraphicsState before = g.Save();
            g.SetClip(screen);
            using (LinearGradientBrush glow = new LinearGradientBrush(screen,
                Color.FromArgb(22, 53, 57), Color.FromArgb(18, 64, 65), 0f))
                g.FillRectangle(glow, screen);
            using (Pen scan = new Pen(Color.FromArgb(36, 74, 76)))
                for (int y = screen.Top + Px(3); y < screen.Bottom; y += Math.Max(3, Px(5)))
                    g.DrawLine(scan, screen.Left, y, screen.Right, y);

            int inset = Px(screen.Width < Px(410) ? 14 : 22);
            bool showHint = screen.Height >= Px(73);
            int pixel = Math.Min(Px(5), Math.Min((screen.Width - Px(212)) / 23, (screen.Height - Px(12)) / 12));
            int fishX = screen.Right - inset - 21 * pixel;
            int textRight = pixel >= 2 ? fishX - Px(6) : screen.Right - inset;
            int textWidth = Math.Max(1, textRight - screen.Left - inset);
            const string directions = "拖动空白处移动，移到顶边显示遥控条。";
            bool wrapHint = showHint && TextRenderer.MeasureText(g, directions, bodyFont).Width > textWidth;
            int headingH = headingFont.Height + Px(6);
            int lineH = showHint ? bodyFont.Height + Px(3) : 0;
            int hintH = wrapHint ? lineH * 2 : lineH;
            int top = screen.Top + Math.Max(0, (screen.Height - headingH - hintH) / 2);
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            Rectangle line = new Rectangle(screen.Left + inset, top, textWidth, headingH);
            TextRenderer.DrawText(g, "频道选择", headingFont, line, Paper, flags);
            line.Offset(0, headingH);
            line.Height = lineH;
            Color hintColor = Color.FromArgb(186, 216, 205);
            if (showHint) TextRenderer.DrawText(g, wrapHint ? "拖动空白处移动窗口" : directions,
                bodyFont, line, hintColor, flags);
            if (wrapHint)
            {
                line.Offset(0, lineH);
                TextRenderer.DrawText(g, "移到顶边显示遥控条", bodyFont, line, hintColor, flags);
            }
            if (pixel >= 2) DrawFish(g, fishX,
                screen.Top + (screen.Height - 11 * pixel) / 2, pixel);
            g.Restore(before);
            using (Pen edge = new Pen(Color.FromArgb(53, 99, 96)))
                g.DrawRectangle(edge, screen.X, screen.Y, screen.Width - 1, screen.Height - 1);
        }

        private static readonly string[] Fish = {
            "         +++          ",
            "        +##++         ",
            "       +#####+++      ",
            "  ++  +#########+     ",
            "  +#++#####.##.##+   .",
            "   +#######.##.###+   ",
            "  +#++##########+++ . ",
            "  ++  +#*******++     ",
            "       +++***+++      ",
            "          +#++        ",
            "           ++         "
        };

        private static void DrawFish(Graphics g, int left, int top, int px)
        {
            using (SolidBrush outline = new SolidBrush(Color.FromArgb(24, 90, 84)))
            using (SolidBrush scales = new SolidBrush(Mint))
            using (SolidBrush belly = new SolidBrush(Color.FromArgb(179, 238, 208)))
            using (SolidBrush eye = new SolidBrush(Ink))
            using (SolidBrush bubble = new SolidBrush(Color.FromArgb(94, 166, 149)))
            {
                for (int y = 0; y < Fish.Length; ++y)
                    for (int x = 0; x < Fish[y].Length; ++x)
                    {
                        char c = Fish[y][x];
                        if (c == ' ') continue;
                        Brush brush = c == '+' ? outline : c == '#' ? scales : c == '*' ? belly : x > 18 ? bubble : eye;
                        g.FillRectangle(brush, left + x * px, top + y * px, px, px);
                    }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            novelMenu.Dispose();
            articleMenu.Dispose();
            musicMenu.Dispose();
            hint.Dispose();
            headingFont.Dispose();
            bodyFont.Dispose();
            keyFont.Dispose();
            compactKeyFont.Dispose();
            logoFont.Dispose();
        }

        private sealed class ChannelKey : Button
        {
            private static readonly Color[] colors = {
                Color.FromArgb(255, 102, 106), Color.FromArgb(254, 148, 187), Color.FromArgb(136, 222, 104),
                Color.FromArgb(93, 209, 238), Color.FromArgb(113, 189, 255),
                Color.FromArgb(134, 222, 201), Color.FromArgb(229, 204, 144)
            };
            private readonly int channel;
            private readonly Font markFont;
            private bool hot;

            public ChannelKey(int channel, Font markFont)
            {
                this.channel = channel;
                this.markFont = markFont;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Surface;
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (channel >= 4 && (keyData == (Keys.Alt | Keys.Down) || keyData == Keys.Apps))
                {
                    PerformClick();
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(hot ? Color.FromArgb(35, 58, 67) : Surface);
                bool focus = Focused && ShowFocusCues;
                using (Pen edge = new Pen(hot || focus ? Mint : Line, focus ? 2 : 1))
                    g.DrawRectangle(edge, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
                if (channel >= 4)
                {
                    float unit = DeviceDpi / 96f;
                    float x = Width - 9 * unit;
                    float y = 9 * unit;
                    using (Pen chevron = new Pen(Mint, Math.Max(1f, unit)))
                        g.DrawLines(chevron, new[] { new PointF(x - 3 * unit, y), new PointF(x, y + 3 * unit), new PointF(x + 3 * unit, y) });
                }
                int textH = Font.Height + 3;
                float scale = Math.Min(DeviceDpi / 96f, Math.Min((Height - textH - 6) / 25f, (Width - 10) / 29f));
                if (scale > 0)
                {
                    int stackH = (int)(24 * scale) + textH + 3;
                    int stackTop = Math.Max(2, (Height - stackH) / 2);
                    GraphicsState before = g.Save();
                    g.TranslateTransform(Width / 2f, stackTop + 12 * scale);
                    g.ScaleTransform(scale, scale);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawMark(g);
                    g.Restore(before);
                    TextRenderer.DrawText(g, Text, Font, new Rectangle(0, stackTop + (int)(24 * scale) + 3, Width, textH),
                        Paper, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }
            }

            private void DrawMark(Graphics g)
            {
                using (SolidBrush color = new SolidBrush(colors[channel]))
                using (SolidBrush cutout = new SolidBrush(Ink))
                using (Pen pen = new Pen(colors[channel], 2.1f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    switch (channel)
                    {
                        case 0:
                            g.FillRectangle(color, -11, -8, 22, 16);
                            g.FillEllipse(color, -14, -8, 7, 16);
                            g.FillEllipse(color, 7, -8, 7, 16);
                            g.FillPolygon(cutout, new Point[] { new Point(-3, -4), new Point(5, 0), new Point(-3, 4) });
                            break;
                        case 1:
                            g.DrawRectangle(pen, -10, -6, 20, 14);
                            g.DrawLine(pen, -6, -11, -2, -7);
                            g.DrawLine(pen, 6, -11, 2, -7);
                            g.DrawLine(pen, -6, -1, -3, 0);
                            g.DrawLine(pen, 3, 0, 6, -1);
                            break;
                        case 2:
                            g.DrawRectangle(pen, -13, -8, 26, 16);
                            using (StringFormat align = new StringFormat())
                            {
                                align.Alignment = StringAlignment.Center;
                                align.LineAlignment = StringAlignment.Center;
                                g.DrawString("iQIYI", markFont, color, new RectangleF(-14, -8, 28, 17), align);
                            }
                            break;
                        case 3:
                            g.FillPolygon(color, new Point[] { new Point(-9, -11), new Point(13, 0), new Point(-9, 11) });
                            g.FillPolygon(cutout, new Point[] { new Point(-5, -5), new Point(5, 0), new Point(-5, 5) });
                            break;
                        case 4:
                            g.DrawLines(pen, new Point[] { new Point(-5, 5), new Point(-5, -7), new Point(8, -10), new Point(8, 3) });
                            g.DrawLine(pen, -4, -4, 8, -7);
                            g.FillEllipse(color, -12, 3, 8, 6);
                            g.FillEllipse(color, 1, 1, 8, 6);
                            break;
                        case 5:
                            g.DrawPolygon(pen, new[] { new Point(-12,-8), new Point(-6,-8), new Point(0,-5),
                                new Point(6,-8), new Point(12,-8), new Point(12,7), new Point(6,7),
                                new Point(0,10), new Point(-6,7), new Point(-12,7) });
                            g.DrawLine(pen, 0, -5, 0, 9);
                            break;
                        case 6:
                            g.DrawRectangle(pen, -9, -10, 18, 20);
                            for (int y = -5; y <= 5; y += 5) g.DrawLine(pen, -5, y, y == 5 ? 1 : 5, y);
                            break;
                    }
                }
            }
        }
    }
}
