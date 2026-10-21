using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KillerMCP.Notify;

public partial class RepairConfirmWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining = 270;
    private int _armDelay = 2;

    public bool Approved { get; private set; }

    private RepairConfirmWindow(Request request)
    {
        InitializeComponent();
        TaskbarIdentity.Track(this);
        GrainLayer.Background = UpdateWindow.CreateGrain();
        PaneGrain.Background = GrainLayer.Background;
        TitleText.Text = request.Title;
        EffectsText.Text = request.Effects;
        CommandText.Text = request.Command;
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) =>
        {
            Activate();
            DenyButton.Focus();
            _timer.Start();
            UpdateTimer();
        };
    }

    public static RepairConfirmWindow? TryCreate(string encoded)
    {
        try
        {
            var request = JsonSerializer.Deserialize<Request>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            if (request is null || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Command)) return null;
            return new RepairConfirmWindow(request);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_armDelay > 0 && --_armDelay == 0) ApproveButton.IsEnabled = true;
        if (--_remaining <= 0)
        {
            _timer.Stop();
            Close();
            return;
        }
        UpdateTimer();
    }

    private void UpdateTimer() => TimerText.Text = $"Denies in {_remaining / 60}:{_remaining % 60:00}";

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        Approved = true;
        _timer.Stop();
        Close();
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private sealed record Request(string Action, string Title, string Tier, string Effects, string Target, string Command);
}
