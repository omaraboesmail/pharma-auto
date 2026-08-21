using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace PharmaAuto.Connector.ControlUi;

public partial class MainWindow : Window
{
    private readonly ControlViewModel viewModel = new();
    private ControlApiClient? client;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => client?.Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await RunBusyAsync(async cancellationToken =>
        {
            client = new ControlApiClient(ControlUiSettings.Load());
            await RefreshAsync(cancellationToken);
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await RunBusyAsync(RefreshAsync);
    }

    private async void CreatePairing_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await RunBusyAsync(async cancellationToken =>
        {
            var pairing = await RequireClient().CreatePairingAsync(cancellationToken);
            viewModel.PairingQr = CreateQr(pairing.QrPayload);
            viewModel.PairingExpiry = $"Expires {pairing.ExpiresAt.ToLocalTime():t}";
            viewModel.PairingHint =
                "Scan with the Android camera, then open Pharma Auto. " +
                "The certificate fingerprint is pinned in this code.";
            viewModel.StatusMessage = "One-time pairing code created. It has not paired a device yet.";
            viewModel.StatusMessageBrush = Brushes.DarkGreen;
        });
    }

    private async void RebuildCatalog_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await RunBusyAsync(async cancellationToken =>
        {
            viewModel.StatusMessage = "Reading the Genius catalog without writes…";
            viewModel.StatusMessageBrush = Brushes.DarkGreen;
            var summary = await RequireClient().RebuildCatalogAsync(cancellationToken);
            ApplyCatalog(summary);
            viewModel.StatusMessage =
                $"Catalog projection completed: {summary.ItemCount:N0} Items, " +
                $"{summary.VendorCount:N0} Vendors. Genius writes remained disabled.";
            await RefreshAsync(cancellationToken);
        });
    }

    private async void RevokeDevice_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not FrameworkElement { DataContext: DeviceRow device } || !device.CanRevoke)
        {
            return;
        }
        var decision = MessageBox.Show(
            this,
            $"Revoke {device.DisplayName}? The device will need a new one-time pairing code.",
            "Revoke paired device",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }
        await RunBusyAsync(async cancellationToken =>
        {
            await RequireClient().RevokeDeviceAsync(device.DeviceId, cancellationToken);
            viewModel.StatusMessage = $"{device.DisplayName} was revoked.";
            viewModel.StatusMessageBrush = Brushes.DarkGreen;
            await RefreshAsync(cancellationToken);
        });
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var api = RequireClient();
        var healthTask = api.GetHealthAsync(cancellationToken);
        var jobsTask = api.GetJobsAsync(cancellationToken);
        var devicesTask = api.GetDevicesAsync(cancellationToken);
        await Task.WhenAll(healthTask, jobsTask, devicesTask);

        var health = await healthTask
            ?? throw new InvalidOperationException("Connector health response was empty.");
        viewModel.ServiceStatus = "Online / متصل";
        viewModel.StatusBrush = new SolidColorBrush(Color.FromRgb(7, 93, 53));
        viewModel.PharmacyName = health.PharmacyDisplayName;
        viewModel.BaseUrl = health.BaseUrl;
        viewModel.QueueDepth = health.QueueDepth.ToString(CultureInfo.CurrentCulture);
        if (health.Catalog is not null)
        {
            ApplyCatalog(health.Catalog);
        }

        Replace(
            viewModel.Jobs,
            (await jobsTask ?? []).Select(job => new JobRow(
                job.JobId,
                HumanizeState(job.State),
                $"{job.UploadedPageCount}/{job.PageCount}",
                job.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                job.FailureCode ?? "—")));
        Replace(
            viewModel.Devices,
            (await devicesTask ?? []).Select(device => new DeviceRow(
                device.DeviceId,
                device.DisplayName,
                device.RevokedAt is null ? "Active / نشط" : "Revoked / ملغى",
                device.RevokedAt is null)));
        viewModel.LastRefreshed = $"Updated {DateTimeOffset.Now:t}";
        viewModel.StatusMessage =
            health.GeniusWritesEnabled
                ? "Unsafe configuration: Genius writes unexpectedly enabled."
                : "Healthy. Read-only workflow is available; Genius writes are disabled.";
        viewModel.StatusMessageBrush = health.GeniusWritesEnabled
            ? new SolidColorBrush(Color.FromRgb(186, 26, 26))
            : Brushes.DarkGreen;
    }

    private void ApplyCatalog(CatalogSummary summary)
    {
        viewModel.ItemCount = summary.ItemCount.ToString("N0", CultureInfo.CurrentCulture);
        viewModel.IdentifierCount = (summary.BarcodeCount + summary.VendorCodeCount)
            .ToString("N0", CultureInfo.CurrentCulture);
        viewModel.UntrustedLabelCount = summary.UntrustedLabelCount
            .ToString("N0", CultureInfo.CurrentCulture);
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> operation)
    {
        viewModel.IsBusy = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            viewModel.ServiceStatus = "Needs attention / يحتاج مراجعة";
            viewModel.StatusBrush = new SolidColorBrush(Color.FromRgb(138, 81, 0));
            viewModel.StatusMessage = "The operation timed out. Check that the Connector service is running.";
            viewModel.StatusMessageBrush = new SolidColorBrush(Color.FromRgb(138, 81, 0));
        }
        catch (Exception exception)
        {
            viewModel.ServiceStatus = "Offline / غير متصل";
            viewModel.StatusBrush = new SolidColorBrush(Color.FromRgb(186, 26, 26));
            viewModel.StatusMessage = exception.Message;
            viewModel.StatusMessageBrush = new SolidColorBrush(Color.FromRgb(186, 26, 26));
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private ControlApiClient RequireClient() => client
        ?? throw new InvalidOperationException("Connector control client is not initialized.");

    private static BitmapImage CreateQr(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8, drawQuietZones: true);
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string HumanizeState(string state) => state
        .Replace('_', ' ')
        .ToLowerInvariant()
        .Replace("ocr", "OCR", StringComparison.OrdinalIgnoreCase);
}

public sealed class ControlViewModel : INotifyPropertyChanged
{
    private string serviceStatus = "Connecting… / جارٍ الاتصال…";
    private Brush statusBrush = new SolidColorBrush(Color.FromRgb(138, 81, 0));
    private string pharmacyName = "Local Connector";
    private string baseUrl = "—";
    private string queueDepth = "—";
    private string itemCount = "—";
    private string identifierCount = "—";
    private string untrustedLabelCount = "—";
    private BitmapImage? pairingQr;
    private string pairingExpiry = "No active pairing code";
    private string pairingHint = "Create a code when the Android device is ready.";
    private string statusMessage = "Connecting to the local service…";
    private Brush statusMessageBrush = Brushes.DimGray;
    private string lastRefreshed = string.Empty;
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<JobRow> Jobs { get; } = [];

    public ObservableCollection<DeviceRow> Devices { get; } = [];

    public string ServiceStatus { get => serviceStatus; set => Set(ref serviceStatus, value); }

    public Brush StatusBrush { get => statusBrush; set => Set(ref statusBrush, value); }

    public string PharmacyName { get => pharmacyName; set => Set(ref pharmacyName, value); }

    public string BaseUrl { get => baseUrl; set => Set(ref baseUrl, value); }

    public string QueueDepth { get => queueDepth; set => Set(ref queueDepth, value); }

    public string ItemCount { get => itemCount; set => Set(ref itemCount, value); }

    public string IdentifierCount { get => identifierCount; set => Set(ref identifierCount, value); }

    public string UntrustedLabelCount
    {
        get => untrustedLabelCount;
        set => Set(ref untrustedLabelCount, value);
    }

    public BitmapImage? PairingQr
    {
        get => pairingQr;
        set
        {
            if (Set(ref pairingQr, value))
            {
                OnPropertyChanged(nameof(QrPlaceholderVisibility));
            }
        }
    }

    public Visibility QrPlaceholderVisibility =>
        PairingQr is null ? Visibility.Visible : Visibility.Collapsed;

    public string PairingExpiry { get => pairingExpiry; set => Set(ref pairingExpiry, value); }

    public string PairingHint { get => pairingHint; set => Set(ref pairingHint, value); }

    public string StatusMessage { get => statusMessage; set => Set(ref statusMessage, value); }

    public Brush StatusMessageBrush
    {
        get => statusMessageBrush;
        set => Set(ref statusMessageBrush, value);
    }

    public string LastRefreshed { get => lastRefreshed; set => Set(ref lastRefreshed, value); }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (Set(ref isBusy, value))
            {
                OnPropertyChanged(nameof(BusyVisibility));
            }
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record JobRow(
    Guid JobId,
    string State,
    string Pages,
    string Updated,
    string FailureCode);

public sealed record DeviceRow(
    Guid DeviceId,
    string DisplayName,
    string Status,
    bool CanRevoke);
