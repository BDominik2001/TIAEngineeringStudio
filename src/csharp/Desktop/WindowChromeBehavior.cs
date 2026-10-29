using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TiaStudio.Desktop
{
    /// <summary>
    /// Az egyedi ablakkeret (PortalWindow stílus) fejléc-gombjainak parancskötései:
    /// SystemCommands.Minimize/Maximize/Restore/Close, valamint a maximalizált méret.
    /// </summary>
    /// <remarks>
    /// WindowChrome-mal a Windows a maximalizált ablakot a láthatatlan keret vastagságával a képernyőn
    /// túlra helyezi; ez a vastagság a DPI-skálázástól függ, ezért fix margóval (pl. 150%-on) a fejléc
    /// teteje levágódott. A WM_GETMINMAXINFO-ban az ablak pontosan a monitor munkaterületét kapja
    /// (tálca nélkül), így semmi nem lóg le, bármilyen DPI-n és több monitoron is.
    /// </remarks>
    public static class WindowChromeBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(WindowChromeBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

        private const int WmGetMinMaxInfo = 0x0024;
        private const int MonitorDefaultToNearest = 0x00000002;

        public static bool GetIsEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject element, bool value)
        {
            element.SetValue(IsEnabledProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is Window window) || !(bool)e.NewValue)
            {
                return;
            }
            window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, a) => SystemCommands.CloseWindow(window)));
            window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, a) => SystemCommands.MinimizeWindow(window)));
            window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, a) => SystemCommands.MaximizeWindow(window)));
            window.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (s, a) => SystemCommands.RestoreWindow(window)));
            window.SourceInitialized += (s, a) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(WndProc);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmGetMinMaxInfo)
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    MinMaxInfo minMax = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
                    minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                    minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                    minMax.MaxSize.X = info.Work.Right - info.Work.Left;
                    minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                    Marshal.StructureToPtr(minMax, lParam, false);
                }
                // handled = false: a WPF saját feldolgozása ezután alkalmazza a MinWidth/MinHeight korlátot.
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public int Flags;
        }
    }
}
