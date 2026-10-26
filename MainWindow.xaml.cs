using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Whitebox.Core;
using Whitebox.Services;
using Whitebox.ViewModels;
using Whitebox.Views;

namespace Whitebox;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, FrameworkElement> _pages = [];

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowStyle();
        StateChanged += (_, _) => OnStateChanged();
        Loaded += async (_, _) =>
        {
            Navigate(App.StartPage ?? "home");
            if (App.ScreenshotPath != null) await DevScreenshotAsync(App.ScreenshotPath);
        };

        Notifier.Raised += ShowToast;
        Dialogs.ConfirmHandler = ShowConfirm;
        Navigator.Handler = Navigate;

        bool admin = AdminHelper.IsAdmin;
        AdminText.Text = admin ? "Administrator" : "Standard mode";
        AdminGlyph.Foreground = (Brush)FindResource(admin ? "Success" : "Warning");
        AdminSub.Text = admin
            ? "Full access to system cleanup and repair tools."
            : "Some system areas need admin rights to clean.";
        AdminButton.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        VersionText.Text = $"Whitebox v{App.Version}";
    }

    private void Project_Click(object sender, RoutedEventArgs e) => Shell.Open(App.ProjectUrl);

    // ---------------- navigation ----------------

    private static FrameworkElement CreatePage(string key) => key switch
    {
        "home" => new HomeView { DataContext = new HomeViewModel() },
        "cleanup" => new CleanupView { DataContext = new CleanupViewModel() },
        "large" => new LargeFilesView { DataContext = new LargeFilesViewModel() },
        "dupes" => new DuplicatesView { DataContext = new DuplicatesViewModel() },
        "space" => new SpaceLensView { DataContext = new SpaceLensViewModel() },
        "performance" => new PerformanceView { DataContext = new PerformanceViewModel() },
        "startup" => new StartupView { DataContext = new StartupViewModel() },
        "apps" => new UninstallerView { DataContext = new UninstallerViewModel() },
        "tools" => new ToolsView { DataContext = new ToolsViewModel() },
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && sender is FrameworkElement { Tag: string key }) ShowPage(key);
    }

    public void Navigate(string key)
    {
        var radio = NavPanel.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == key);
        if (radio == null) return;
        if (radio.IsChecked == true) ShowPage(key);
        else radio.IsChecked = true;
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = CreatePage(key);
            _pages[key] = page;
        }
        if (PageHost.Content == page) return;

        PageHost.Content = page;
        var translate = new TranslateTransform(0, 14);
        page.RenderTransform = translate;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
    }

    /// <summary>Developer aid: optionally runs the page's scan, renders the window to a PNG, then exits.</summary>
    private async Task DevScreenshotAsync(string path)
    {
        await Task.Delay(800);
        if (App.RunScan && PageHost.Content is FrameworkElement { DataContext: { } vm }
            && vm.GetType().GetProperty("ScanCommand")?.GetValue(vm) is System.Windows.Input.ICommand scan)
        {
            scan.Execute(null);
            await Task.Delay(App.ScanWaitMs);
        }
        await Task.Delay(1200);
        var dpi = VisualTreeHelper.GetDpi(this);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(this);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using (var fs = File.Create(path)) enc.Save(fs);
        Application.Current.Shutdown();
    }

    // ---------------- window chrome ----------------

    private void ApplyWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));     // DWMWA_USE_IMMERSIVE_DARK_MODE
        int round = 2;
        Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));    // DWMWA_WINDOW_CORNER_PREFERENCE = round
        int border = 0x003A2620;                                           // COLORREF (BGR) for #20263A
        Native.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));   // DWMWA_BORDER_COLOR
    }

    private void OnStateChanged()
    {
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void AdminButton_Click(object sender, RoutedEventArgs e) => AdminHelper.RestartElevated();

    // ---------------- toasts ----------------

    private void ShowToast(string title, string message, ToastKind kind)
    {
        var (glyph, brushKey) = kind switch
        {
            ToastKind.Success => ("", "GradClean"),
            ToastKind.Warning => ("", "GradStartup"),
            ToastKind.Error => ("", "GradDanger"),
            _ => ("", "AccentGradient"),
        };

        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource(brushKey),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), Foreground = Brushes.White, FontSize = 15 },
        };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("H3") });
        if (!string.IsNullOrWhiteSpace(message))
            text.Children.Add(new TextBlock
            {
                Text = message, Style = (Style)FindResource("Sub"), TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None, Margin = new Thickness(0, 2, 0, 0),
            });

        var panel = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);
        panel.Children.Add(text);

        var toast = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x2E)),
            BorderBrush = (Brush)FindResource("StrokeStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 16, 12),
            Margin = new Thickness(0, 10, 0, 0),
            Child = panel,
            Opacity = 0,
            RenderTransform = new TranslateTransform(40, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.45, Color = Colors.Black },
        };
        toast.MouseLeftButtonUp += (_, _) => RemoveToast(toast);

        ToastHost.Children.Add(toast);
        while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        ((TranslateTransform)toast.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 7 : 4.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RemoveToast(toast);
        };
        timer.Start();
    }

    private void RemoveToast(Border toast)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => ToastHost.Children.Remove(toast);
        toast.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------- confirm dialog ----------------

    private bool ShowConfirm(string title, string message, string confirmText, bool danger)
    {
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogOk.Content = confirmText;
        DialogOk.Style = (Style)FindResource(danger ? "DangerButton" : "PrimaryButton");
        DialogIcon.Background = (Brush)FindResource(danger ? "GradDanger" : "AccentGradient");
        DialogGlyph.Text = danger ? "" : "";

        Overlay.Visibility = Visibility.Visible;
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        var pop = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        DialogOk.Focus();

        bool result = false;
        var frame = new DispatcherFrame();
        void Ok(object s, RoutedEventArgs e) { result = true; frame.Continue = false; }
        void Cancel(object s, RoutedEventArgs e) { frame.Continue = false; }
        void Outside(object s, System.Windows.Input.MouseButtonEventArgs e) { if (e.OriginalSource == Overlay) frame.Continue = false; }

        DialogOk.Click += Ok;
        DialogCancel.Click += Cancel;
        Overlay.MouseLeftButtonDown += Outside;
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            DialogOk.Click -= Ok;
            DialogCancel.Click -= Cancel;
            Overlay.MouseLeftButtonDown -= Outside;
            Overlay.Visibility = Visibility.Collapsed;
        }
        return result;
    }
}
