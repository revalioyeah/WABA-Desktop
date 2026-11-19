using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WABA.Domain.Models;
using WABA.Infrastructure.Windows;

namespace WABA.Desktop;

public partial class MainWindow : Window
{
    private readonly WindowCatalog _windowCatalog = new();
    private readonly DesktopCaptureService _captureService = new();
    private readonly MouseInputService _mouseInputService = new();
    private readonly ObservableCollection<GameWindowInfo> _windows = new();
    private readonly ObservableCollection<string> _logs = new();
    private readonly DispatcherTimer _previewTimer;

    private bool _livePreviewRunning;
    private int _frameCount;

    public MainWindow()
    {
        InitializeComponent();

        TargetWindowComboBox.ItemsSource = _windows;
        ActivityLogListBox.ItemsSource = _logs;

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _previewTimer.Tick += (_, _) => CaptureSelectedWindow(isLive: true);

        RefreshWindows();
        AddLog("WABA Desktop Phase 1 siap.");
        AddLog("Capture mode: visible desktop client-area capture.");
        AddLog("OCR dan auto execution belum diaktifkan pada fase ini.");
    }

    private GameWindowInfo? SelectedTarget
        => TargetWindowComboBox.SelectedItem as GameWindowInfo;

    private void RefreshWindows_Click(object sender, RoutedEventArgs e)
        => RefreshWindows();

    private void RefreshWindows()
    {
        var previousHandle = SelectedTarget?.Handle;
        var candidates = _windowCatalog.GetVisibleWindows();

        _windows.Clear();
        foreach (var candidate in candidates)
            _windows.Add(candidate);

        var restored = _windows.FirstOrDefault(w => w.Handle == previousHandle);
        if (restored is not null)
            TargetWindowComboBox.SelectedItem = restored;
        else if (_windows.Count > 0)
            TargetWindowComboBox.SelectedIndex = 0;

        AddLog($"Daftar jendela diperbarui: {_windows.Count} jendela terlihat.");
        FooterStatusText.Text = $"Ditemukan {_windows.Count} jendela yang dapat dipilih.";
    }

    private void TargetWindowComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SelectedTarget is not { } target)
        {
            TargetDetailsText.Text = "Belum ada jendela dipilih.";
            return;
        }

        TargetDetailsText.Text =
            $"{target.Title}\nProcess: {target.ProcessName}\n" +
            $"Client area: {target.ClientWidth} × {target.ClientHeight}\n" +
            $"Screen origin: ({target.ScreenLeft}, {target.ScreenTop})";

        AddLog($"Target dipilih: {target.Title}");
        FooterStatusText.Text = "Target dipilih. Pastikan game terlihat dan tidak tertutup.";
    }

    private void CaptureOnce_Click(object sender, RoutedEventArgs e)
        => CaptureSelectedWindow(isLive: false);

    private void LivePreview_Click(object sender, RoutedEventArgs e)
    {
        if (!_livePreviewRunning)
        {
            if (SelectedTarget is null)
            {
                MessageBox.Show("Pilih jendela game terlebih dahulu.",
                    "Target belum dipilih", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _livePreviewRunning = true;
            _previewTimer.Start();
            LivePreviewButton.Content = "■  Stop live preview";
            PreviewStateText.Text = "LIVE";
            AddLog("Live preview dimulai (4 FPS).");
            CaptureSelectedWindow(isLive: true);
        }
        else
        {
            _previewTimer.Stop();
            _livePreviewRunning = false;
            LivePreviewButton.Content = "▶  Mulai live preview";
            PreviewStateText.Text = "PAUSED";
            AddLog("Live preview dihentikan.");
        }
    }

    private void CaptureSelectedWindow(bool isLive)
    {
        var target = SelectedTarget;
        if (target is null)
        {
            if (!isLive)
                MessageBox.Show("Pilih jendela game terlebih dahulu.");
            return;
        }

        try
        {
            var frame = _captureService.CaptureClientArea(target.Handle);
            var bitmap = BitmapSource.Create(
                frame.Width,
                frame.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                frame.BgraPixels,
                frame.Stride);
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            PreviewPlaceholderText.Visibility = Visibility.Collapsed;

            _frameCount++;
            CaptureInfoText.Text =
                $"{frame.Width} × {frame.Height}  •  Frame #{_frameCount}  •  {frame.CapturedAt:HH:mm:ss.fff}";
            PreviewStateText.Text = isLive ? "LIVE" : "CAPTURED";
            FooterStatusText.Text = $"Capture berhasil: {frame.Width}×{frame.Height}.";
        }
        catch (Exception ex)
        {
            PreviewStateText.Text = "CAPTURE ERROR";
            FooterStatusText.Text = ex.Message;
            if (!isLive)
                AddLog($"Capture gagal: {ex.Message}");
        }
    }

    private async void TestClick_Click(object sender, RoutedEventArgs e)
    {
        var target = SelectedTarget;
        if (target is null)
        {
            MessageBox.Show("Pilih jendela game terlebih dahulu.");
            return;
        }

        if (!int.TryParse(CoordinateXTextBox.Text, out var x) ||
            !int.TryParse(CoordinateYTextBox.Text, out var y))
        {
            MessageBox.Show("Masukkan koordinat X dan Y berupa angka.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"WABA akan mengirim SATU KLIK KIRI sungguhan ke game:\n\n" +
            $"Target: {target.Title}\nClient coordinate: X={x}, Y={y}\n\n" +
            "Pastikan koordinat sudah diperiksa pada preview. Lanjutkan?",
            "Konfirmasi manual input test",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        FooterStatusText.Text = "Mengirim klik uji...";
        var result = await Task.Run(() => _mouseInputService.ClickClientPoint(target, x, y));

        AddLog(result.Message);
        FooterStatusText.Text = result.Message;

        MessageBox.Show(
            result.Message,
            result.Success ? "Input test" : "Input test gagal",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void AddLog(string message)
    {
        _logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_logs.Count > 100)
            _logs.RemoveAt(_logs.Count - 1);
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewTimer.Stop();
        base.OnClosed(e);
    }
}
