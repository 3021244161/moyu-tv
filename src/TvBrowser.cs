using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MoyuTv
{
    public sealed class TvBrowser : UserControl
    {
        private sealed class Page
        {
            public WebView2 View;
            public CoreWebView2 Core;
            public readonly List<CoreWebView2Frame> Frames = new List<CoreWebView2Frame>();
            public Uri Address;
            public string Title = "";
            public string Failure;
            public bool Busy = true;
            public bool HasHistory;
            public bool Fullscreen;
            public bool Theater;
            public bool Reading;
            public string ReaderSuppressed;
            public int ReaderRequest;
            public bool NeedsRestart;
            public ulong NavigationId;
        }

        private readonly List<Page> pages = new List<Page>();
        private readonly TableLayoutPanel errorPanel;
        private readonly Label errorText;
        private Task<CoreWebView2Environment> environmentTask;
        private bool muted;
        private double zoom = 1.0;
        private bool togglingMedia;
        private bool togglingFullscreen;
        private bool autoFillVideo = true;
        private bool preferReading;
        private int readerTextSize = 20;

        public event EventHandler Changed;
        public event EventHandler FullscreenChanged;
        public event Action<string> Notice;
        public event KeyEventHandler PlayerKeyDown;

        private Page Active { get { return pages.Count == 0 ? null : pages[pages.Count - 1]; } }
        public bool IsReady { get { return Active != null && Active.Core != null && !Active.NeedsRestart; } }
        public bool IsBusy { get { return Active != null && Active.Busy; } }
        public bool CanGoBack { get { return Active != null && (pages.Count > 1 || Active.HasHistory); } }
        public bool IsContentFullscreen { get { return Active != null && (Active.Fullscreen || Active.Theater); } }
        public bool IsReading { get { return Active != null && Active.Reading; } }
        public bool PreferReading
        {
            get { return preferReading; }
            set
            {
                if (preferReading == value) return;
                preferReading = value;
                foreach (Page page in pages)
                {
                    page.ReaderRequest++;
                    if (page.Core != null && !page.NeedsRestart) ConfigureDocumentSettings(page);
                }
            }
        }
        public int ReaderTextSize
        {
            get { return readerTextSize; }
            set
            {
                readerTextSize = Math.Min(32, Math.Max(16, value));
                if (IsReady && IsReading)
                    RunDocument(Active.Core.ExecuteScriptAsync, ReaderPresentation.Api +
                        "?.setSize(" + readerTextSize.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }
        public bool AutoFillVideo
        {
            get { return autoFillVideo; }
            set
            {
                if (autoFillVideo == value) return;
                autoFillVideo = value;
                foreach (Page page in pages)
                    if (page.Core != null && !page.NeedsRestart) ConfigureDocumentSettings(page);
            }
        }
        public string PageTitle { get { return Active == null ? "" : Active.Title; } }
        public Uri CurrentUrl { get { return Active == null ? null : Active.Address; } }

        public TvBrowser()
        {
            BackColor = Color.FromArgb(25, 29, 29);
            errorPanel = new TableLayoutPanel {
                Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
                Visible = false, BackColor = BackColor, Padding = new Padding(12)
            };
            errorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            errorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            errorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            errorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            errorText = new Label {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomCenter,
                ForeColor = Color.WhiteSmoke, UseMnemonic = false, Padding = new Padding(0, 0, 0, 8)
            };
            Button retry = new Button {
                Anchor = AnchorStyles.None, Text = "重新加载", AccessibleName = "重新加载页面",
                Size = new Size(94, 30), FlatStyle = FlatStyle.Flat,
                ForeColor = Color.WhiteSmoke, BackColor = Color.FromArgb(48, 57, 54), Cursor = Cursors.Hand
            };
            retry.Click += delegate { Reload(); };
            errorPanel.Controls.Add(errorText, 0, 0);
            errorPanel.Controls.Add(retry, 0, 1);
            Controls.Add(errorPanel);
        }

        private static bool WebAddress(Uri address)
        {
            return address != null && address.IsAbsoluteUri && !String.IsNullOrEmpty(address.Host)
                && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);
        }

        private static bool WebAddress(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && WebAddress(uri);
        }

        private static bool Blank(string value)
        {
            return String.IsNullOrEmpty(value) || String.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase);
        }

        private void Notify(string text)
        {
            Action<string> handler = Notice;
            if (handler != null) handler(text);
        }

        private void RefreshState()
        {
            if (IsDisposed || Disposing) return;
            Page page = Active;
            bool showError = page != null && page.Failure != null;
            errorPanel.Visible = showError;
            if (showError)
            {
                errorText.Text = page.Failure;
                errorPanel.BringToFront();
            }
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void FullscreenStateChanged()
        {
            EventHandler handler = FullscreenChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private Page AddPage(Uri address)
        {
            Page previous = Active;
            if (previous != null)
            {
                previous.View.Hide();
                if (previous.Core != null && !previous.NeedsRestart) previous.Core.IsMuted = true;
            }
            Page page = new Page { Address = address, View = new WebView2 {
                Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor, AllowExternalDrop = false
            } };
            pages.Add(page);
            page.View.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (page != Active || e.Modifiers != Keys.None || (e.KeyCode != Keys.F11 && e.KeyCode != Keys.Escape)) return;
                KeyEventHandler handler = PlayerKeyDown;
                if (handler != null) handler(this, e);
            };
            Controls.Add(page.View);
            page.View.BringToFront();
            RefreshState();
            FullscreenStateChanged();
            return page;
        }

        private async Task Initialize(Page page)
        {
            if (environmentTask == null || environmentTask.IsFaulted || environmentTask.IsCanceled)
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MoyuTV", "WebView2");
                environmentTask = CoreWebView2Environment.CreateAsync(null, folder);
            }
            CoreWebView2Environment environment = await environmentTask;
            if (!pages.Contains(page)) return;
            await page.View.EnsureCoreWebView2Async(environment);
            if (!pages.Contains(page)) return;
            page.Core = page.View.CoreWebView2;
            page.Core.Settings.AreHostObjectsAllowed = false;
            // The document can only notify the toolbar about its own presentation state.
            page.Core.Settings.IsWebMessageEnabled = true;
            page.Core.Settings.IsZoomControlEnabled = false;
            page.Core.Settings.IsStatusBarEnabled = false;
            page.Core.Settings.IsBuiltInErrorPageEnabled = false;
            page.Core.IsMuted = page != Active || muted;
            page.View.ZoomFactor = zoom;
            WireEvents(page);
        }

        private void WireEvents(Page page)
        {
            CoreWebView2 core = page.Core;
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
                if (!Blank(e.Uri) && !WebAddress(e.Uri))
                {
                    e.Cancel = true;
                    if (page == Active && e.IsUserInitiated) Notify("请使用网页内的播放入口。");
                    return;
                }
                if (WebAddress(e.Uri)) page.Address = new Uri(e.Uri);
                if (page.Address != null && page.ReaderSuppressed != page.Address.AbsoluteUri)
                    page.ReaderSuppressed = null;
                page.ReaderRequest++;
                page.NavigationId = e.NavigationId;
                page.Busy = true;
                page.Failure = null;
                if (page == Active) RefreshState();
            };
            core.ContentLoading += delegate {
                bool notifyFullscreen = page.Theater;
                page.Theater = false;
                page.Reading = false;
                if (page == Active && notifyFullscreen) FullscreenStateChanged();
                if (page == Active) RefreshState();
            };
            core.DOMContentLoaded += delegate {
                ConfigureDocument(page, core.ExecuteScriptAsync);
                ScheduleReader(page, false);
            };
            core.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
                Uri source;
                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out source) || !WebAddress(source) ||
                    page.Address == null || source != page.Address) return;
                string state = e.WebMessageAsJson;
                if (state == "\"moyu.reader.on\"" || state == "\"moyu.reader.off\"" || state == "\"moyu.reader.stale\"")
                {
                    SetReadingState(page, state == "\"moyu.reader.on\"");
                    if (state == "\"moyu.reader.off\"") { page.ReaderSuppressed = source.AbsoluteUri; page.ReaderRequest++; }
                    else if (state == "\"moyu.reader.stale\"") ScheduleReader(page, true);
                    return;
                }
                if (state != "\"moyu.layout.on\"" && state != "\"moyu.layout.off\"") return;
                page.Theater = state == "\"moyu.layout.on\"";
                if (page == Active) FullscreenStateChanged();
            };
            core.FrameNavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
                Uri target;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out target)) return;
                if (!WebAddress(target) && target.Scheme != "blob" && target.Scheme != "data"
                    && !Blank(e.Uri) && e.Uri != "about:srcdoc") e.Cancel = true;
            };
            core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) {
                if (e.NavigationId != page.NavigationId) return;
                page.Busy = false;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    string host = page.Address == null ? "网页" : page.Address.Host;
                    page.Failure = "暂时无法打开 " + host + Environment.NewLine + e.WebErrorStatus;
                }
                if (page == Active) RefreshState();
            };
            core.SourceChanged += delegate(object sender, CoreWebView2SourceChangedEventArgs e) {
                Uri location;
                if (Uri.TryCreate(core.Source, UriKind.Absolute, out location) && WebAddress(location))
                    page.Address = location;
                if (page == Active) RefreshState();
                RunDocument(core.ExecuteScriptAsync, VideoPresentation.Api + "?.schedule()");
                if (!e.IsNewDocument) ScheduleReader(page, true);
            };
            core.HistoryChanged += delegate {
                page.HasHistory = core.CanGoBack;
                if (page == Active) RefreshState();
            };
            core.DocumentTitleChanged += delegate {
                page.Title = core.DocumentTitle;
                if (page == Active) RefreshState();
            };
            core.ContainsFullScreenElementChanged += delegate {
                page.Fullscreen = core.ContainsFullScreenElement;
                if (page == Active) FullscreenStateChanged();
            };
            core.FrameCreated += delegate(object sender, CoreWebView2FrameCreatedEventArgs e) {
                CoreWebView2Frame frame = e.Frame;
                page.Frames.Add(frame);
                frame.DOMContentLoaded += delegate { ConfigureDocument(page, frame.ExecuteScriptAsync); };
                frame.Destroyed += delegate { page.Frames.Remove(frame); };
            };
            core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
                OpenPopup(page, e);
            };
            core.WindowCloseRequested += delegate {
                if (pages.Contains(page) && pages.IndexOf(page) > 0)
                    BeginInvoke((Action)delegate { ClosePage(page); });
            };
            core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e) {
                if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.BrowserProcessExited
                    && e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited) return;
                environmentTask = null;
                page.NeedsRestart = true;
                page.Busy = false;
                page.Fullscreen = false;
                page.Theater = false;
                page.Reading = false;
                page.Failure = "网页播放器已停止，点击重新加载可恢复当前页面。";
                if (page == Active) { RefreshState(); FullscreenStateChanged(); }
            };
        }

        private async void OpenPopup(Page opener, CoreWebView2NewWindowRequestedEventArgs request)
        {
            request.Handled = true;
            if (opener != Active || !request.IsUserInitiated || (!Blank(request.Uri) && !WebAddress(request.Uri))) return;
            Uri address;
            Uri.TryCreate(request.Uri, UriKind.Absolute, out address);
            CoreWebView2Deferral deferral = request.GetDeferral();
            Page popup = null;
            try
            {
                popup = AddPage(WebAddress(address) ? address : null);
                await Initialize(popup);
                // Supplying a fresh WebView preserves window.opener for login and payment dialogs.
                if (popup == Active && pages.Contains(opener) && popup.Core != null)
                    request.NewWindow = popup.Core;
            }
            catch (Exception)
            {
                if (popup != null && pages.Contains(popup))
                {
                    ClosePage(popup);
                    Notify("暂时无法打开这个页面，请重试。");
                }
            }
            finally
            {
                try { deferral.Complete(); }
                catch (System.Runtime.InteropServices.COMException) { /* Opener closed during initialization. */ }
                catch (InvalidOperationException) { /* Browser closed while a popup was starting. */ }
            }
        }

        private void ClosePage(Page page)
        {
            bool wasActive = page == Active;
            if (!pages.Remove(page)) return;
            page.View.Dispose();
            if (wasActive && Active != null)
            {
                Active.View.Show();
                Active.View.BringToFront();
                if (Active.Core != null && !Active.NeedsRestart) Active.Core.IsMuted = muted;
            }
            RefreshState();
            FullscreenStateChanged();
        }

        public async void Open(Uri url)
        {
            if (!WebAddress(url)) { Notify("请输入以 https:// 或 http:// 开头的网址。"); return; }
            while (pages.Count > 1) ClosePage(Active);
            if (Active != null && Active.NeedsRestart) ClosePage(Active);
            Page page = Active;
            try
            {
                if (page == null)
                {
                    page = AddPage(url);
                    await Initialize(page);
                    if (!pages.Contains(page)) return;
                }
                else
                {
                    // A second selection while WebView starts wins over the first selection.
                    page.Address = url;
                    if (page.Core == null) { RefreshState(); return; }
                }
                page.Failure = null;
                page.Busy = true;
                page.Core.Navigate(page.Address.AbsoluteUri);
                RefreshState();
            }
            catch (Exception ex)
            {
                if (page != null && pages.Contains(page))
                {
                    page.Busy = false;
                    page.NeedsRestart = true;
                    page.Failure = ex is WebView2RuntimeNotFoundException
                        ? "未找到 Microsoft Edge WebView2 运行时，请安装后重试。"
                        : "播放器启动失败，点击重新加载可再试一次。" + Environment.NewLine + ex.Message;
                    RefreshState();
                }
            }
        }

        public void GoBack()
        {
            Page page = Active;
            if (page == null) return;
            if (!page.NeedsRestart && page.Core != null && page.HasHistory) page.Core.GoBack();
            else if (pages.Count > 1) ClosePage(page);
        }

        public void Reload()
        {
            Page page = Active;
            if (page == null) return;
            if (page.NeedsRestart)
            {
                if (page.Address != null) Open(page.Address);
                else if (pages.Count > 1) GoBack();
                return;
            }
            if (page.Core != null)
            {
                if (page.Failure != null && page.Address != null) page.Core.Navigate(page.Address.AbsoluteUri);
                else page.Core.Reload();
            }
        }

        public void SetMuted(bool value)
        {
            muted = value;
            if (IsReady) Active.Core.IsMuted = value;
        }

        public void SetZoom(double factor)
        {
            if (Double.IsNaN(factor) || Double.IsInfinity(factor)) return;
            zoom = Math.Min(3.0, Math.Max(0.25, factor));
            foreach (Page page in pages)
                if (page.Core != null && !page.NeedsRestart) page.View.ZoomFactor = zoom;
        }

        // Some platforms host their player in a child frame. Pause an existing stream first.
        private const string PauseScript =
            "(function(){const m=Array.from(document.querySelectorAll('video,audio')).find(x=>!x.paused&&!x.ended);" +
            "if(!m)return false;m.pause();return true})()";
        private const string PlayScript =
            "(function(){const list=Array.from(document.querySelectorAll('video,audio')).filter(x=>x.currentSrc||x.srcObject);" +
            "const m=list.find(x=>x.currentTime>0&&!x.ended)||list.find(x=>x.getBoundingClientRect().width>0)||list[0];" +
            "if(!m)return false;m.play().catch(()=>{});return true})()";
        private const string IsPlayingScript =
            "Array.from(document.querySelectorAll('video,audio')).some(x=>!x.paused&&!x.ended)";

        private static List<Func<string, Task<string>>> Documents(Page page)
        {
            var documents = new List<Func<string, Task<string>>> { page.Core.ExecuteScriptAsync };
            foreach (var frame in page.Frames.ToArray()) documents.Add(frame.ExecuteScriptAsync);
            return documents;
        }

        private void ConfigureDocument(Page page, Func<string, Task<string>> run)
        {
            bool enabled = autoFillVideo && !preferReading && !page.Reading;
            RunDocument(run, VideoPresentation.Install + "\n" + VideoPresentation.Api +
                ".setEnabled(" + (enabled ? "true" : "false") + ");");
        }

        private void ConfigureDocumentSettings(Page page)
        {
            foreach (var run in Documents(page)) ConfigureDocument(page, run);
        }

        private void SetReadingState(Page page, bool reading)
        {
            if (!pages.Contains(page) || page.Reading == reading) return;
            page.Reading = reading;
            if (page.Core != null && !page.NeedsRestart) ConfigureDocumentSettings(page);
            if (page == Active) RefreshState();
        }

        private async Task<bool> OpenReader(Page page)
        {
            Uri address = page.Address;
            ulong navigation = page.NavigationId;
            string script = ReaderPresentation.Install + "\n" + ReaderPresentation.Api +
                ".open(" + readerTextSize.ToString(CultureInfo.InvariantCulture) + ")";
            string result = await TryDocument(page.Core.ExecuteScriptAsync, script);
            if (page != Active || page.NavigationId != navigation || page.Address != address || result != "true") return false;
            SetReadingState(page, true);
            return true;
        }

        private async void ScheduleReader(Page page, bool sameDocumentNavigation)
        {
            if (!preferReading || page != Active || page.Address == null) return;
            int request = ++page.ReaderRequest;
            string address = page.Address.AbsoluteUri;
            // Some article sites render the new chapter after their SPA address changes.
            foreach (int delay in new[] { sameDocumentNavigation ? 300 : 0, 500, 1100, 2000 })
            {
                if (delay != 0) await Task.Delay(delay);
                if (page != Active || !preferReading || page.NeedsRestart || page.Reading ||
                    request != page.ReaderRequest || page.ReaderSuppressed == address ||
                    page.Address == null || page.Address.AbsoluteUri != address) return;
                if (await OpenReader(page)) return;
            }
        }

        public async Task<bool> ToggleReaderAsync()
        {
            if (!IsReady) return false;
            Page page = Active;
            page.ReaderRequest++;
            if (!page.Reading)
            {
                if (IsContentFullscreen)
                {
                    await TryDocument(page.Core.ExecuteScriptAsync, VideoPresentation.Api + "?.leave();");
                    if (page != Active) return false;
                }
                page.ReaderSuppressed = null;
                return await OpenReader(page);
            }
            string result = await TryDocument(page.Core.ExecuteScriptAsync,
                "(function(){const reader=" + ReaderPresentation.Api +
                ";if(!reader)return false;reader.close();return !reader.isOpen()})()");
            if (page != Active || result != "true") return false;
            page.ReaderSuppressed = page.Address == null ? null : page.Address.AbsoluteUri;
            SetReadingState(page, false);
            return true;
        }

        private static async void RunDocument(Func<string, Task<string>> run, string script)
        {
            await TryDocument(run, script);
        }

        // A player can replace its iframe during any asynchronous document call.
        private static async Task<string> TryDocument(Func<string, Task<string>> run, string script)
        {
            try { return await run(script); }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (OperationCanceledException) { return null; }
        }

        private async Task<Func<string, Task<string>>> FindVideo(Page page)
        {
            Func<string, Task<string>> selected = null;
            double bestScore = 0;
            foreach (var run in Documents(page))
            {
                if (page != Active || page.NeedsRestart) return null;
                string value = await TryDocument(run, VideoPresentation.Api + "?.score()");
                double score;
                if (Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out score) && score > bestScore)
                {
                    bestScore = score;
                    selected = run;
                }
            }
            return page == Active ? selected : null;
        }

        public async Task<string> GetVideoQualityInfoAsync()
        {
            Page page = Active;
            if (!IsReady) return null;
            var run = await FindVideo(page);
            if (run == null || page != Active) return null;
            string result = await TryDocument(run, VideoPresentation.Api + "?.quality()");
            if (page != Active || String.IsNullOrEmpty(result) || result == "null") return null;
            return result.Trim('"').Replace("x", " × ");
        }

        public async Task<bool> OpenQualityMenuAsync()
        {
            Page page = Active;
            if (!IsReady) return false;
            var run = await FindVideo(page);
            if (run == null || page != Active) return false;
            var documents = Documents(page);
            // An iframe may hold the video while its parent renders the controls.
            documents.Remove(run);
            documents.Insert(0, run);
            foreach (var document in documents)
            {
                string state = await TryDocument(document, PlatformQuality.OpenMenuScript);
                foreach (int delay in new[] { 180, 350, 400, 450 })
                {
                    if (state != "\"pending\"" || page != Active) break;
                    await Task.Delay(delay);
                    if (page != Active) return false;
                    state = await TryDocument(document, PlatformQuality.OpenMenuScript);
                }
                if (page != Active) return false;
                if (state == "true") return true;
            }
            return false;
        }

        public async void ToggleVideoFullscreen()
        {
            if (!IsReady || togglingFullscreen) return;
            togglingFullscreen = true;
            Page page = Active;
            try
            {
                page.View.Focus();
                if (IsContentFullscreen)
                {
                    await TryDocument(page.Core.ExecuteScriptAsync, VideoPresentation.Api + "?.leave();");
                    return;
                }
                var run = await FindVideo(page);
                if (page != Active) return;
                if (run != null)
                {
                    await TryDocument(run, VideoPresentation.Api + "?.manual();");
                    await Task.Delay(350);
                }
                if (page == Active && !IsContentFullscreen) Notify("请点击网页播放器自己的全屏按钮。");
            }
            finally { togglingFullscreen = false; }
        }

        public async void TogglePlay()
        {
            if (!IsReady || togglingMedia) return;
            Page page = Active;
            togglingMedia = true;
            try
            {
                var documents = Documents(page);
                foreach (string script in new[] { PauseScript, PlayScript })
                {
                    foreach (var run in documents)
                    {
                        if (page != Active || IsDisposed) return;
                        try
                        {
                            if (await run(script) != "true") continue;
                            if (script == PlayScript)
                            {
                                await Task.Delay(500);
                                if (page == Active && await run(IsPlayingScript) != "true")
                                    Notify("请在网页播放器里点击播放。");
                            }
                            return;
                        }
                        catch (System.Runtime.InteropServices.COMException) { /* Navigated frame. */ }
                        catch (InvalidOperationException) { /* Closed frame. */ }
                    }
                }
                if (page == Active) Notify("请先在网页里选择视频或歌曲。");
            }
            finally { togglingMedia = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pages.Clear();
            base.Dispose(disposing);
        }
    }
}
