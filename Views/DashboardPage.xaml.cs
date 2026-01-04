namespace SkanyxxMaui.Views;

public partial class DashboardPage : ContentPage
{
    private IDispatcherTimer? _timer;

    public DashboardPage()
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
        TimeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _timer?.Start();
        UpdateTime();
    }

    private async void OnStartInvestigationClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//investigate");
    }

    private async void OnViewAgentsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//agents");
    }

    private async void OnCheckAlertsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//alerts");
    }
}
