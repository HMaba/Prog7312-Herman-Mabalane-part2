using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartxWPF.Models;
using SmartxWPF.Services;

namespace SmartxWPF;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly AuthenticationService _authentication;
    private readonly DashboardService _dashboard;
    private readonly DeviceService _devices;
    private readonly CommandStreamService _commands;
    private readonly FileImportService _imports;
    private readonly TelemetryService _telemetry;
    private bool _loading;
    private bool _updatingDevices;
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    public ObservableCollection<SensorDto> Sensors { get; } = new();
    public ObservableCollection<SensorTimelineDto> Timeline { get; } = new();

    public ObservableCollection<DeviceDto> Devices { get; } = new();
    public ObservableCollection<AlertDto> Alerts { get; } = new();
    public ObservableCollection<InsightDto> Insights { get; } = new();
    public ObservableCollection<CommandDto> Commands { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        _authentication = new AuthenticationService(_api);
        _dashboard = new DashboardService(_api);
        _devices = new DeviceService(_api);
        _commands = new CommandStreamService(_api);
        _imports = new FileImportService(_api);
        _telemetry = new TelemetryService(_api);
        TelemetrySensors.ItemsSource = Sensors;
        TelemetryTimeline.ItemsSource = Timeline;
        OverviewDevices.ItemsSource = Devices;
        TelemetryDevices.ItemsSource = Devices;
        AlertsGrid.ItemsSource = Alerts;
        HistoryGrid.ItemsSource = Commands;
        OverviewInsights.ItemsSource = Insights;
        _liveTimer.Tick += async (_, _) => await RefreshAllAsync(live: true);
    }

    private void Enter_Click(object sender, RoutedEventArgs e)
    {
        SplashPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoginEmailBox.Focus();
    }

    private void BackToWelcome_Click(object sender, RoutedEventArgs e)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        SplashPanel.Visibility = Visibility.Visible;
        LoginMessage.Text = "Email/Password sign-in must be enabled in Firebase Authentication.";
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        ApiUrlBox.Text = LoginApiUrlBox.Text.Trim();
        EmailBox.Text = LoginEmailBox.Text.Trim();
        PasswordBox.Password = LoginPasswordBox.Password;
        await SignInAndOpenWorkspaceAsync(ApiUrlBox.Text, EmailBox.Text, PasswordBox.Password);
    }

    private async void ApplyConnection_Click(object sender, RoutedEventArgs e)
    {
        await SignInAndOpenWorkspaceAsync(ApiUrlBox.Text, EmailBox.Text, PasswordBox.Password);
    }

    private async Task SignInAndOpenWorkspaceAsync(string apiUrl, string email, string password)
    {
        try
        {
            LoginMessage.Text = "Signing in securely…";
            SetStatus("Signing in to Firebase…");
            await _authentication.SignInAsync(apiUrl.Trim(), email.Trim(), password);
            ConnectionStatus.Text = "Signed in";
            ConnectionStatus.Foreground = Brushes.LightGreen;
            LoginPanel.Visibility = Visibility.Collapsed;
            SplashPanel.Visibility = Visibility.Collapsed;
            WorkspaceGrid.Visibility = Visibility.Visible;
            ConnectionPanel.Visibility = Visibility.Collapsed;
            _liveTimer.Start();
            SetStatus($"Firebase sign-in successful for {_authentication.SignedInEmail}. Connecting to SmartX API…");
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = "Not connected";
            ConnectionStatus.Foreground = Brushes.OrangeRed;
            LoginMessage.Text = ex.Message;
            SetStatus(ex.Message);
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _liveTimer.Stop();
        _api.Configure(ApiUrlBox.Text.Trim(), null);
        PasswordBox.Clear();
        LoginPasswordBox.Clear();
        WorkspaceGrid.Visibility = Visibility.Collapsed;
        SplashPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        ConnectionPanel.Visibility = Visibility.Collapsed;
        ConnectionStatus.Text = "Not connected";
        ConnectionStatus.Foreground = Brushes.OrangeRed;
        LoginMessage.Text = "You have signed out. Sign in again to reconnect.";
    }

    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Sign in with an account created in Firebase Authentication. Email/Password must be enabled under Firebase Console → Authentication → Sign-in method.\n\nThe resulting Firebase ID token is sent to SmartXApi as a Bearer token. The API must be configured with Firebase project ID 'smar-auth'.",
            "Smart-X Firebase sign-in", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async Task RefreshAllAsync(bool live = false)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (!live) SetStatus("Connecting to SmartX API…");
            var dashboard = await _dashboard.GetDashboardAsync();
            ReplaceDevicesKeepingSelection(dashboard.Devices);
            Replace(Alerts, dashboard.RecentAlerts.Count > 0 ? dashboard.RecentAlerts : dashboard.ActiveAlerts);
            Replace(Insights, dashboard.Insights);
            DeviceCount.Text = dashboard.Stats.RegisteredDevices.ToString();
            AlertCount.Text = dashboard.Stats.ActiveErrorStates.ToString();
            PacketCount.Text = dashboard.Stats.ProcessedPackets.ToString("N0");
            DisconnectedCount.Text = dashboard.Stats.DisconnectedNodes.ToString();
            ConnectionStatus.Text = "Connected";
            ConnectionStatus.Foreground = Brushes.LightGreen;
            SetStatus(live
                ? $"Live. Updated {DateTime.Now:T}. {Devices.Count} device(s), {dashboard.Stats.ProcessedPackets:N0} packets processed."
                : $"Connected. Dashboard updated at {dashboard.GeneratedAt.ToLocalTime():g}. {Devices.Count} device(s) returned.");
            if (MainTabs.SelectedIndex == 2) await LoadCommandDataAsync();
            if (live && MainTabs.SelectedIndex == 1 && TelemetryDevices.SelectedItem is DeviceDto selected) await RefreshTimelineQuietAsync(selected);
        }
        catch (HttpRequestException ex)
        {
            ConnectionStatus.Text = "Connection failed";
            ConnectionStatus.Foreground = Brushes.OrangeRed;
            SetStatus($"Could not load the API. Check that SmartXApi is running at {_api.BaseUrl}, and that the Firebase ID token is valid. {ex.Message}");
        }
        catch (TaskCanceledException) { SetStatus("The API request timed out. Check the API process and URL."); }
        catch (Exception ex) { SetStatus($"Unable to load dashboard: {ex.Message}"); }
        finally { _loading = false; }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _devices.SearchAsync(SearchBox.Text.Trim());
            Replace(Devices, result);
            SetStatus($"Search completed. {result.Count} matching device(s). Search activity is recorded by the API.");
        }
        catch (Exception ex) { SetStatus($"Device search failed: {ex.Message}"); }
    }

    private async void ImportDevices_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import Smart-X device list", Filter = "Device lists (*.txt;*.csv)|*.txt;*.csv|Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SetStatus($"Importing {System.IO.Path.GetFileName(dialog.FileName)}…");
            var result = await _imports.ImportAsync(dialog.FileName);
            await RefreshAllAsync();
            var errors = result.Errors.Count == 0 ? "" : $" {result.Errors.Count} line error(s). First: line {result.Errors[0].Line}: {result.Errors[0].Message}";
            SetStatus($"Import complete: {result.Created} created, {result.Updated} updated, {result.Skipped} skipped.{errors}");
        }
        catch (Exception ex) { SetStatus($"Device import failed: {ex.Message}"); }
    }

    private void TelemetryNav_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;
    private void CommandNav_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;
    private void DashboardNav_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 0;

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != MainTabs) return;
        PageTitle.Text = MainTabs.SelectedIndex switch { 0 => "Operations overview", 1 => "Sensor telemetry", 2 => "Real-time command stream & history", _ => "Smart-X operations" };
        if (MainTabs.SelectedIndex == 2) await LoadCommandDataAsync();
        else if (MainTabs.SelectedIndex == 0) await RefreshAllAsync();
    }

    private void TelemetryDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDevices) return;
        if (TelemetryDevices.SelectedItem is DeviceDto device)
        {
            CommandDeviceId.Text = device.DeviceId.ToString();
            CommandSensorId.Text = device.Sensors.FirstOrDefault()?.Id.ToString() ?? "";
            Replace(Sensors, device.Sensors);
            _ = LoadTimelineAsync(device, device.Sensors.FirstOrDefault());
        }
    }

    private async Task LoadTimelineAsync(DeviceDto device, SensorDto? sensor)
    {
        if (sensor is null) return;
        try
        {
            var timeline = await _telemetry.GetTimelineAsync(device.DeviceId, sensor.Id, 30);
            Replace(Timeline, timeline);
            SetStatus($"Loaded {Sensors.Count} sensor(s) for {device.DeviceIdentifier}; {timeline.Count} historical point(s) for {sensor.Name}.");
        }
        catch (Exception ex) { SetStatus($"Sensor timeline unavailable: {ex.Message}"); }
    }

    private async void TurnOn_Click(object sender, RoutedEventArgs e) => await SendCommandAsync(0);
    private async void TurnOff_Click(object sender, RoutedEventArgs e) => await SendCommandAsync(1);
    private async void Toggle_Click(object sender, RoutedEventArgs e) => await SendCommandAsync(2);

    private async Task SendCommandAsync(int command)
    {
        if (!int.TryParse(CommandDeviceId.Text, out var deviceId) || deviceId <= 0) { SetStatus("Select a device or enter a valid numeric device ID."); return; }
        int? sensorId = int.TryParse(CommandSensorId.Text, out var parsedSensor) && parsedSensor > 0 ? parsedSensor : null;
        try
        {
            var outcome = await _commands.SendAsync(new CommandRequestDto { DeviceId = deviceId, SensorId = sensorId, Command = command });
            SetStatus(outcome.Message);
            await LoadCommandDataAsync();
            await RefreshAllAsync();
        }
        catch (Exception ex) { SetStatus($"Command failed: {ex.Message}"); }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var outcome = await _commands.UndoAsync();
            SetStatus(outcome.Message);
            await LoadCommandDataAsync();
            await RefreshAllAsync();
        }
        catch (Exception ex) { SetStatus($"Undo failed: {ex.Message}"); }
    }

    private async void History_Click(object sender, RoutedEventArgs e) => await LoadCommandDataAsync();

    private async Task LoadCommandDataAsync()
    {
        try
        {
            Replace(Alerts, await _dashboard.GetAlertsAsync());
            Replace(Commands, await _commands.GetHistoryAsync());
        }
        catch (Exception ex) { SetStatus($"Unable to load alerts or command history: {ex.Message}"); }
    }

    // Live refresh rebuilds the device list every few seconds; keep the user's selected device (and its sensors) in place.
    private void ReplaceDevicesKeepingSelection(IEnumerable<DeviceDto> items)
    {
        var selectedId = (TelemetryDevices.SelectedItem as DeviceDto)?.DeviceId;
        _updatingDevices = true;
        try
        {
            Replace(Devices, items);
            if (selectedId.HasValue)
            {
                var match = Devices.FirstOrDefault(d => d.DeviceId == selectedId.Value);
                TelemetryDevices.SelectedItem = match;
                if (match is not null) Replace(Sensors, match.Sensors);
            }
        }
        finally { _updatingDevices = false; }
    }

    private async Task RefreshTimelineQuietAsync(DeviceDto device)
    {
        var sensor = device.Sensors.FirstOrDefault();
        if (sensor is null) return;
        try { Replace(Timeline, await _telemetry.GetTimelineAsync(device.DeviceId, sensor.Id, 30)); }
        catch (Exception) { /* the next tick retries */ }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
    private void SetStatus(string message) => StatusMessage.Text = message;
    protected override void OnClosed(EventArgs e) { _liveTimer.Stop(); _api.Dispose(); base.OnClosed(e); }
}
