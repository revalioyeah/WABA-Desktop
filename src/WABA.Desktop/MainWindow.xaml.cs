using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WABA.Domain.Models;
using WABA.Infrastructure.Windows;
using WABA.Recognition.Models;
using WABA.Recognition.Services;

namespace WABA.Desktop;

public partial class MainWindow : Window
{
    private readonly WindowCatalog _windowCatalog = new();
    private readonly DesktopCaptureService _captureService = new();
    private readonly MouseInputService _mouseInputService = new();
    private readonly OrderPanelScanner _orderPanelScanner = new();
    private readonly OrderPanelChangeDetector _orderChangeDetector = new();
    private readonly ObservableCollection<GameWindowInfo> _windows = new();
    private readonly ObservableCollection<string> _logs = new();
    private readonly DispatcherTimer _previewTimer;

    private CapturedFrame? _lastFrame;
    private bool _livePreviewRunning;
    private bool _autoOrderScanEnabled;
    private int _frameCount;
    private int _captureBusy;
    private int _scanBusy;
    private DateTime _lastAutoScanUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        TargetWindowComboBox.ItemsSource = _windows;
        ActivityLogListBox.ItemsSource = _logs;

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _previewTimer.Tick += async (_, _) => await CaptureSelectedWindowAsync(isLive: true);

        RefreshWindows();
        OrderEngineText.Text = _orderPanelScanner.EngineDescription;
        AddLog("WABA Desktop Phase 2 siap.");
        AddLog("Capture berjalan terpisah dari proses OCR.");
        AddLog("OCR memakai Windows OCR dan bahasa yang tersedia di perangkat.");
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

        _lastFrame = null;
        _orderChangeDetector.Reset();

        TargetDetailsText.Text =
            $"{target.Title}\nProcess: {target.ProcessName}\n" +
            $"Client area: {target.ClientWidth} × {target.ClientHeight}\n" +
            $"Screen origin: ({target.ScreenLeft}, {target.ScreenTop})";

        AddLog($"Target dipilih: {target.Title}");
        FooterStatusText.Text = "Target dipilih. Pastikan game terlihat dan tidak tertutup.";
    }

    private async void CaptureOnce_Click(object sender, RoutedEventArgs e)
        => await CaptureSelectedWindowAsync(isLive: false);

    private async void LivePreview_Click(object sender, RoutedEventArgs e)
    {
        if (!_livePreviewRunning)
            await StartLivePreviewAsync();
        else
            StopLivePreview();
    }

    private async Task StartLivePreviewAsync()
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
        await CaptureSelectedWindowAsync(isLive: true);
    }

    private void StopLivePreview()
    {
        _previewTimer.Stop();
        _livePreviewRunning = false;
        LivePreviewButton.Content = "▶  Mulai live preview";
        PreviewStateText.Text = "PAUSED";

        if (AutoOrderScanCheckBox.IsChecked == true)
            AutoOrderScanCheckBox.IsChecked = false;

        AddLog("Live preview dihentikan.");
    }

    private async Task CaptureSelectedWindowAsync(bool isLive)
    {
        var target = SelectedTarget;
        if (target is null)
        {
            if (!isLive)
                MessageBox.Show("Pilih jendela game terlebih dahulu.");
            return;
        }

        if (Interlocked.Exchange(ref _captureBusy, 1) != 0)
            return;

        try
        {
            // GDI screen capture runs off the WPF UI thread so preview rendering stays responsive.
            var frame = await Task.Run(() => _captureService.CaptureClientArea(target.Handle));
            _lastFrame = frame;

            var bitmap = ToBitmapSource(frame);
            PreviewImage.Source = bitmap;
            PreviewPlaceholderText.Visibility = Visibility.Collapsed;

            _frameCount++;
            CaptureInfoText.Text =
                $"{frame.Width} × {frame.Height}  •  Frame #{_frameCount}  •  {frame.CapturedAt:HH:mm:ss.fff}";
            PreviewStateText.Text = isLive ? "LIVE" : "CAPTURED";
            FooterStatusText.Text = $"Capture berhasil: {frame.Width}×{frame.Height}.";

            if (_autoOrderScanEnabled)
                TryStartAutoScan(frame);
        }
        catch (Exception ex)
        {
            PreviewStateText.Text = "CAPTURE ERROR";
            FooterStatusText.Text = ex.Message;
            if (!isLive)
                AddLog($"Capture gagal: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _captureBusy, 0);
        }
    }

    private void TryStartAutoScan(CapturedFrame frame)
    {
        try
        {
            var region = ReadOrderPanelRegion().ToPixels(frame.Width, frame.Height);
            if (!_orderChangeDetector.HasChanged(frame, region))
                return;

            var now = DateTime.UtcNow;
            if (now - _lastAutoScanUtc < TimeSpan.FromMilliseconds(450))
                return;

            _lastAutoScanUtc = now;
            _ = ScanOrderPanelAsync(frame, isAutomatic: true);
        }
        catch (Exception ex)
        {
            OrderScanStatusText.Text = $"ROI tidak valid: {ex.Message}";
        }
    }

    private async void ScanOrderPanel_Click(object sender, RoutedEventArgs e)
    {
        var frame = _lastFrame;
        if (frame is null)
        {
            await CaptureSelectedWindowAsync(isLive: false);
            frame = _lastFrame;
        }

        if (frame is null)
            return;

        await ScanOrderPanelAsync(frame, isAutomatic: false);
    }

    private async Task ScanOrderPanelAsync(CapturedFrame frame, bool isAutomatic)
    {
        if (Interlocked.Exchange(ref _scanBusy, 1) != 0)
            return;

        try
        {
            var region = ReadOrderPanelRegion();
            OrderScanStatusText.Text = isAutomatic
                ? "Perubahan panel terdeteksi • OCR berjalan..."
                : "Memindai panel pesanan...";

            var result = await _orderPanelScanner.ScanAsync(frame, region);

            OrderCropPreview.Source = ToBitmapSource(result.CroppedFrame);
            OrderScanMetaText.Text =
                $"ROI {result.Region.X},{result.Region.Y} • {result.Region.Width}×{result.Region.Height} px  |  " +
                $"{result.OcrLanguage}  |  {result.Duration.TotalMilliseconds:0} ms";

            var parsedLines = result.Items.Select(item =>
            {
                var quantity = item.CompletedQuantity.HasValue && item.RequestedQuantity.HasValue
                    ? $"{item.CompletedQuantity}/{item.RequestedQuantity}"
                    : "? / ?";
                var marker = item.IsKnownProduct ? "" : " [BELUM DIKENALI]";
                return $"{item.Name}  {quantity}{marker}";
            });

            var parsedText = result.Items.Count == 0
                ? "Belum ada item yang dapat dipisahkan."
                : string.Join(Environment.NewLine, parsedLines);

            OrderScanOutputText.Text =
                $"HASIL PARSING ({result.Items.Count} item)\n{parsedText}\n\n" +
                $"TEKS OCR MENTAH\n{(string.IsNullOrWhiteSpace(result.RawText) ? "(tidak ada teks terbaca)" : result.RawText)}\n\n" +
                $"TOKEN OCR & POSISI\n{result.WordDiagnostics}";

            OrderScanStatusText.Text = result.Items.Count > 0
                ? $"Selesai • {result.Items.Count} baris terdeteksi. Periksa hasil sebelum digunakan."
                : "OCR selesai, tetapi belum menemukan baris item. Periksa crop ROI.";

            AddLog($"OCR {(isAutomatic ? "otomatis" : "manual")}: {result.Items.Count} item, {result.Duration.TotalMilliseconds:0} ms.");
            FooterStatusText.Text = "Hasil OCR tersedia. Hasil belum digunakan untuk mengirim klik.";
        }
        catch (Exception ex)
        {
            OrderScanStatusText.Text = $"OCR gagal: {ex.Message}";
            AddLog($"OCR gagal: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanBusy, 0);
        }
    }

    private OrderPanelRegion ReadOrderPanelRegion()
    {
        if (!int.TryParse(OrderRoiXTextBox.Text, out var x) ||
            !int.TryParse(OrderRoiYTextBox.Text, out var y) ||
            !int.TryParse(OrderRoiWidthTextBox.Text, out var width) ||
            !int.TryParse(OrderRoiHeightTextBox.Text, out var height))
        {
            throw new InvalidOperationException("Nilai X, Y, W, H harus berupa angka persentase.");
        }

        return new OrderPanelRegion(x, y, width, height);
    }

    private void AutoOrderScanCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (SelectedTarget is null)
        {
            AutoOrderScanCheckBox.IsChecked = false;
            MessageBox.Show("Pilih jendela game terlebih dahulu.");
            return;
        }

        _autoOrderScanEnabled = true;
        _orderChangeDetector.Reset();
        AddLog("Pemantauan panel otomatis diaktifkan. OCR hanya berjalan saat ROI berubah.");

        if (!_livePreviewRunning)
            _ = StartLivePreviewAsync();
    }

    private void AutoOrderScanCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        _autoOrderScanEnabled = false;
        AddLog("Pemantauan panel otomatis dinonaktifkan.");
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

    private static BitmapSource ToBitmapSource(CapturedFrame frame)
    {
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
        return bitmap;
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
