using System.Windows;

namespace KillerMCP.Notify;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        int index = Array.FindIndex(e.Args, value => value.Equals("--confirm-repair", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            StartupUri = new Uri("UpdateWindow.xaml", UriKind.Relative);
            base.OnStartup(e);
            return;
        }

        base.OnStartup(e);
        RepairConfirmWindow? confirm = index + 1 < e.Args.Length ? RepairConfirmWindow.TryCreate(e.Args[index + 1]) : null;
        if (confirm is null)
        {
            Shutdown(1);
            return;
        }
        MainWindow = confirm;
        confirm.Closed += (_, _) => Shutdown(confirm.Approved ? 0 : 1);
        confirm.Show();
    }
}
