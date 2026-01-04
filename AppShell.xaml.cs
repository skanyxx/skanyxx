namespace SkanyxxMaui;

public partial class AppShell : Shell
{
    private IDispatcherTimer? _timer;

    public AppShell()
    {
        InitializeComponent();
        StartClock();
    }

    private void StartClock()
    {
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (s, e) => UpdateTime();
        _timer.Start();
        UpdateTime();
    }

    private void UpdateTime()
    {
        LastCheckedLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private async void OnDashboardTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//dashboard");
    }

    private async void OnInvestigateTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//investigate");
    }

    private async void OnAgentsTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//agents");
    }

    private async void OnChatTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//chat");
    }

    private async void OnCloudToolsTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//cloudtools");
    }

    private async void OnToolServersTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//toolservers");
    }

    private async void OnAnalyticsTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//analytics");
    }

    private async void OnMemoryTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//memory");
    }

    private async void OnAlertsTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//alerts");
    }

    private async void OnHooksTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//hooks");
    }

    private async void OnDebugTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//debug");
    }

    private async void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync("//settings");
    }
}
