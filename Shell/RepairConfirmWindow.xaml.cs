using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KillerMCP.Notify;

public partial class RepairConfirmWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining = 270;
    private int _armDelay = 2;
    private bool _closed;

    public bool Approved { get; private set; }

    private RepairConfirmWindow(RepairApprovalText request)
    {
        InitializeComponent();
        TaskbarIdentity.Track(this);
        GrainLayer.Background = UpdateWindow.CreateGrain();
        PaneGrain.Background = GrainLayer.Background;
        TitleText.Text = request.Title;
        TargetText.Text = request.Target;
        TierText.Text = request.Tier == "Disruptive" ? "Disruptive repair" : "Safe repair";
        EffectsText.Text = request.Effects;
        CommandText.Text = request.Command;
        _timer.Tick += Timer_Tick;
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
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
        var request = RepairApprovalText.Parse(encoded);
        return request is null ? null : new RepairConfirmWindow(request);
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

    private void UpdateTimer() => TimerText.Text = $"Closes in {_remaining / 60}:{_remaining % 60:00}";

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _armDelay > 0) return;
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
}
