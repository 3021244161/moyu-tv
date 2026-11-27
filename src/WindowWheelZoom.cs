using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MoyuTv
{
    internal sealed class WindowWheelZoom : IDisposable, IMessageFilter
    {
        private const int MouseWheel = 0x020A, WheelDelta = 120;
        private const int LowLevelMouse = 14, ControlKey = 0x11, RootAncestor = 2;
        private readonly Form window;
        private readonly Func<bool> isScreenFullscreen;
        private readonly Action restoreWindow;
        private readonly MouseHook callback;
        private IntPtr windowHandle, hook;
        private bool localFilter, posted, disposed;
        private int pendingDelta;
        private Point anchor;

        internal WindowWheelZoom(Form window, Func<bool> isScreenFullscreen, Action restoreWindow)
        {
            this.window = window;
            this.isScreenFullscreen = isScreenFullscreen;
            this.restoreWindow = restoreWindow;
            callback = HandleMouse;
            window.HandleCreated += Attach;
            window.HandleDestroyed += Detach;
            window.Disposed += WindowDisposed;
            if (window.IsHandleCreated) Attach(window, EventArgs.Empty);
        }

        private void Attach(object sender, EventArgs e)
        {
            windowHandle = window.Handle;
            // WebView2 processes its own mouse messages, outside the WinForms message filter.
            hook = SetWindowsHookEx(LowLevelMouse, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                Application.AddMessageFilter(this);
                localFilter = true;
            }
        }

        private IntPtr HandleMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message == (IntPtr)MouseWheel && (GetAsyncKeyState(ControlKey) & 0x8000) != 0)
            {
                var wheel = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                if (QueueWheel(wheel.Position, (short)(wheel.ButtonData >> 16))) return (IntPtr)1;
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            if (message.Msg != MouseWheel || (message.WParam.ToInt64() & 8) == 0) return false;
            long position = message.LParam.ToInt64();
            return QueueWheel(new Point((short)position, (short)(position >> 16)), (short)(message.WParam.ToInt64() >> 16));
        }

        private bool QueueWheel(Point position, int delta)
        {
            if (disposed || windowHandle == IntPtr.Zero || delta == 0 ||
                GetAncestor(WindowFromPoint(position), RootAncestor) != windowHandle) return false;
            anchor = position;
            pendingDelta += delta;
            if (!posted && Math.Abs(pendingDelta) >= WheelDelta)
            {
                try
                {
                    posted = true;
                    window.BeginInvoke((MethodInvoker)ApplyWheel);
                }
                catch (InvalidOperationException)
                {
                    posted = false;
                    pendingDelta = 0;
                    return false;
                }
            }
            return true;
        }

        private void ApplyWheel()
        {
            posted = false;
            int steps = pendingDelta / WheelDelta;
            pendingDelta %= WheelDelta;
            if (steps == 0 || disposed || !window.Visible || window.WindowState == FormWindowState.Minimized) return;
            if (isScreenFullscreen())
            {
                if (steps < 0) restoreWindow();
                return;
            }
            window.Bounds = ScaleAt(window.Bounds, window.MinimumSize, Screen.FromPoint(anchor).WorkingArea, anchor, steps);
        }

        internal static Rectangle ScaleAt(Rectangle frame, Size minimum, Rectangle work, Point point, int steps)
        {
            double minFactor = Math.Max((double)minimum.Width / frame.Width, (double)minimum.Height / frame.Height);
            double maxFactor = Math.Min((double)work.Width / frame.Width, (double)work.Height / frame.Height);
            double factor = Math.Min(maxFactor, Math.Max(minFactor, Math.Pow(1.10, steps)));
            int width = Math.Min(work.Width, Math.Max(minimum.Width, (int)Math.Round(frame.Width * factor)));
            int height = Math.Min(work.Height, Math.Max(minimum.Height, (int)Math.Round(frame.Height * factor)));
            double fractionX = (double)(point.X - frame.Left) / frame.Width;
            double fractionY = (double)(point.Y - frame.Top) / frame.Height;
            int left = Math.Min(work.Right - width, Math.Max(work.Left, point.X - (int)Math.Round(fractionX * width)));
            int top = Math.Min(work.Bottom - height, Math.Max(work.Top, point.Y - (int)Math.Round(fractionY * height)));
            return new Rectangle(left, top, width, height);
        }

        private void WindowDisposed(object sender, EventArgs e) { Dispose(); }

        private void Detach(object sender, EventArgs e)
        {
            windowHandle = IntPtr.Zero;
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (localFilter) { Application.RemoveMessageFilter(this); localFilter = false; }
            posted = false;
            pendingDelta = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Detach(window, EventArgs.Empty);
            window.HandleCreated -= Attach;
            window.HandleDestroyed -= Detach;
            window.Disposed -= WindowDisposed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData
        {
            public Point Position;
            public uint ButtonData, Flags, Time;
            public UIntPtr ExtraInfo;
        }

        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, MouseHook callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string module);
    }
}
