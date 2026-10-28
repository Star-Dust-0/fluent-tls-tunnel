using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace FluentTlsTunnel;
sealed class TrayMenuWindow : Window
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    bool shuttingDown;
    public void Shutdown(){shuttingDown=true;Close();}
    readonly Border _root = new();
    readonly StackPanel _list = new() { Spacing = 2 };
    readonly IntPtr _hwnd;
    readonly Action<int> _onCmd;

    public TrayMenuWindow(Action<int> onCmd)
    {
        _onCmd = onCmd;
        _root.Padding = new Thickness(4);
        _root.Child = _list;
        Content = _root;
        Title = "Fluent TLS Tunnel Menu";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false; p.IsAlwaysOnTop = true;
        }
        try { AppWindow.IsShownInSwitchers = false; } catch { }
        try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { }
        AppWindow.Closing += (a, e) => {if(shuttingDown)return; e.Cancel = true; a.Hide(); };
        Activated += (s, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) AppWindow.Hide(); };
        try
        {
            long st = (long)GetWindowLongPtr(_hwnd, -16);
            st &= ~(0x00C00000L | 0x00800000L | 0x00040000L | 0x00020000L | 0x00010000L);
            SetWindowLongPtr(_hwnd, -16, (IntPtr)st);
            int none = unchecked((int)0xFFFFFFFE), round = 2;
            DwmSetWindowAttribute(_hwnd, 34, ref none, 4);   // no DWM border color
            DwmSetWindowAttribute(_hwnd, 33, ref round, 4);  // rounded corners
        } catch { }
        _root.CornerRadius = new CornerRadius(8);
        _root.BorderThickness = new Thickness(1);
        _root.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 128, 128, 128));
        long ex = (long)GetWindowLongPtr(_hwnd, -20);
        SetWindowLongPtr(_hwnd, -20, (IntPtr)(ex | 0x80));

    }

    void AddItem(string text, string glyph, int cmd, bool check = false)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = new FontIcon { Glyph = glyph, FontSize = 16 };
        var tb = new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 1);
        g.Children.Add(ic); g.Children.Add(tb);
        if (check)
        {
            var ck = new FontIcon { Glyph = "\uE73E", FontSize = 14 };
            Grid.SetColumn(ck, 2);
            g.Children.Add(ck);
        }
        var b = new Button
        {
            Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 0, 12, 0), Height = 36, CornerRadius = new CornerRadius(4),
        };
        b.Click += (s, e) => { AppWindow.Hide(); _onCmd(cmd); };
        _list.Children.Add(b);
    }

    public void ShowAt(int ax, int ay, string theme, bool ar, bool connected, bool busy)
    {
        var wa = DisplayArea.GetFromPoint(new PointInt32(ax, ay), DisplayAreaFallback.Nearest).WorkArea;
        _root.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        _root.FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _list.Children.Clear();
        AddItem(busy ? (ar ? "إلغاء" : "Cancel") : connected ? (ar ? "قطع الاتصال" : "Disconnect") : (ar ? "اتصال" : "Connect"), "\uE71B", 1);
        AddItem(ar ? "الإعدادات" : "Settings", "\uE713", 2);
        AddItem(ar ? "حول التطبيق" : "About", "\uE946", 3);
        _list.Children.Add(new Border { Height = 1, Margin = new Thickness(4,3,4,3), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40,128,128,128)) });
        AddItem(ar ? "إنهاء" : "Quit", "\uE7E8", 4);

        double dpi = GetDpiForWindow(_hwnd) / 96.0;
        if (dpi < 1) dpi = 1;
        int w = (int)(230 * dpi), h = (int)((4 * 36 + 3 * 2 + 7 + 8) * dpi);
        int x = Math.Clamp(ax - w / 2, wa.X + 8, wa.X + wa.Width - w - 8);
        int y = ay > wa.Y + wa.Height / 2 ? wa.Y + wa.Height - h - 8 : wa.Y + 8;
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        AppWindow.Show();
        Activate();
        SetForegroundWindow(_hwnd);
    }
}
