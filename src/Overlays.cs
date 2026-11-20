using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;
using Screen = System.Windows.Forms.Screen;

namespace TeyvatGuide
{
    internal abstract class OverlayWindow : Window
    {
        private const int ExStyle = -20, Transparent = 0x20, NoActivate = 0x08000000;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyle(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetStyle(IntPtr hwnd, int index, IntPtr style);
        [DllImport("user32.dll")] protected static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        public bool Locked { get; private set; }
        public bool AllowClose { get; set; }
        public event Action DismissRequested;
        protected void RequestDismiss() { var handler = DismissRequested; if (handler != null) handler(); }
        protected OverlayWindow()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            ShowActivated = false; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += (s, e) => ApplyStyle();
            Closing += (s, e) => {
                if (AllowClose) return;
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(RequestDismiss));
            };
        }
        public virtual void SetLocked(bool locked)
        {
            Locked = locked;
            ApplyStyle();
        }
        private void ApplyStyle()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            long style = GetStyle(hwnd, ExStyle).ToInt64();
            long updated = Locked ? style | (Transparent | NoActivate) : style & ~(long)(Transparent | NoActivate);
            SetStyle(hwnd, ExStyle, new IntPtr(updated));
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0033); // topmost, no activation or geometry change
        }
        protected void DragOverlay(object sender, MouseButtonEventArgs e)
        {
            if (!Locked && e.LeftButton == MouseButtonState.Pressed) { DragMove(); e.Handled = true; }
        }
        public void Place(double? left, double? top)
        {
            Rect primary = SystemParameters.WorkArea;
            double x = left ?? double.NaN, y = top ?? double.NaN;
            bool visible = false;
            if (!double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(y) && !double.IsInfinity(y))
            {
                var source = PresentationSource.FromVisual(Application.Current.MainWindow);
                var scale = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
                foreach (var monitor in Screen.AllScreens)
                {
                    var b = monitor.WorkingArea;
                    var bounds = new Rect(b.Left / scale.M11, b.Top / scale.M22, b.Width / scale.M11, b.Height / scale.M22);
                    if (bounds.Contains(new Point(x + 24, y + 12))) { visible = true; break; }
                }
            }
            if (!visible)
            {
                x = Math.Max(primary.Left, primary.Right - Width - 20);
                y = primary.Top + 20;
            }
            Left = x; Top = y;
        }
    }

    internal sealed class VideoOverlay : OverlayWindow
    {
        public readonly WebView2CompositionControl Browser;
        private readonly Border bar;
        private readonly TextBlock caption;
        public VideoOverlay()
        {
            Title = "攻略视频 · 提瓦特随行";
            var panel = new DockPanel { Background = Brushes.Black };
            caption = new TextBlock { Text = "攻略视频", Foreground = Brushes.White, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var grip = new TextBlock { Text = "\uE76F", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(0,0,8,0) };
            var heading = new DockPanel { Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = "拖动以移动视频窗" };
            heading.Children.Add(grip); heading.Children.Add(caption); heading.MouseLeftButtonDown += DragOverlay;
            var close = new Button { Name = "OverlayClose", Content = "\uE8BB", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12, ToolTip = "关闭视频", Style = (Style)FindResource("OverlayCloseButton"),
                Padding = new Thickness(0), MinHeight = 0, Width = 36, Focusable = false };
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭视频");
            close.Click += (s,e) => RequestDismiss();
            var title = new DockPanel(); DockPanel.SetDock(close, Dock.Right); title.Children.Add(close); title.Children.Add(heading);
            bar = new Border { Height = 30, Background = (Brush)FindResource("Shell"), Child = title,
                Padding = new Thickness(9,0,1,0) };
            DockPanel.SetDock(bar, Dock.Top); panel.Children.Add(bar);
            Browser = new WebView2CompositionControl();
            panel.Children.Add(Browser);
            Content = panel; SetWidth(480);
        }
        public void SetCaption(string value) { caption.Text = value; }
        public void SetWidth(double width)
        {
            Width = width; Height = width * 9 / 16 + (Locked ? 0 : bar.Height);
        }
        public override void SetLocked(bool locked)
        {
            base.SetLocked(locked);
            bar.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
            Browser.IsHitTestVisible = !locked;
            SetWidth(Width);
        }
    }

}
