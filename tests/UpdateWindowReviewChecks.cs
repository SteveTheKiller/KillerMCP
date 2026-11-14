using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerMCP.Notify;

internal static class UpdateWindowReviewChecks
{
    internal static void Run(string scratch)
    {
        var application = new App();
        application.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        foreach (string offered in new[] { "1.0.0", "2.0.0" })
        {
            var window = new UpdateWindow(new[] { "--installed", "2.0.0", "--latest", offered, "--behavior", "check" });
            try
            {
                if ((bool)typeof(UpdateWindow).GetProperty("OffersUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!)
                    throw new InvalidOperationException("The update window offered same or older version " + offered);
            }
            finally { window.Close(); }
        }
        var newer = new UpdateWindow(new[] { "--installed", "2.0.0", "--latest", "2.0.1", "--behavior", "check" });
        try
        {
            if (!(bool)typeof(UpdateWindow).GetProperty("OffersUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(newer)!)
                throw new InvalidOperationException("The update window refused a newer release.");
        }
        finally { newer.Close(); }
        Console.WriteLine("PASS update window refuses same/older versions and offers newer (3 selected regressions)");
        CheckPreferenceDuringDownload(scratch, false);
        CheckPreferenceDuringDownload(scratch, true);
    }

    private static void CheckPreferenceDuringDownload(string scratch, bool externalChange)
    {
        string preferences = Path.Combine(scratch, "preferences-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(preferences, "{\"Behavior\":\"install\"}");
        var assembly = typeof(UpdateWindow).Assembly;
        var optionsType = assembly.GetType("KillerMCP.Notify.UpdateWindowOptions", true)!;
        var options = Activator.CreateInstance(optionsType, true)!;
        optionsType.GetProperty("PreferencesFile", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(options, preferences);
        var installerType = assembly.GetType("KillerMCP.Notify.UpdateInstaller+VerifiedInstaller", true)!;
        var completionType = typeof(System.Threading.Tasks.TaskCompletionSource<>).MakeGenericType(installerType);
        var completion = Activator.CreateInstance(completionType)!;
        var task = completionType.GetProperty("Task")!.GetValue(completion)!;
        int starts = 0;
        bool finished = false;
        SetBoundary(options, "Download", parameters => Expression.Convert(Expression.Constant(task), optionsType.GetProperty("Download", BindingFlags.Instance | BindingFlags.NonPublic)!.PropertyType.GetMethod("Invoke")!.ReturnType));
        Action markStart = () => starts++;
        Action markDone = () => finished = true;
        SetBoundary(options, "StartInstaller", parameters => Expression.Block(Expression.Invoke(Expression.Constant(markStart)), Expression.Constant(true)));
        SetBoundary(options, "Show", parameters => Expression.Invoke(Expression.Constant(markDone)));
        SetBoundary(options, "Exit", parameters => Expression.Invoke(Expression.Constant(markDone)));
        var constructor = typeof(UpdateWindow).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string[]), optionsType }, null)!;
        var window = (UpdateWindow)constructor.Invoke(new object[] { new[] { "--installed", "2.0.0", "--latest", "2.0.1", "--behavior", "install" }, options });
        try
        {
            typeof(UpdateWindow).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            if (externalChange) File.WriteAllText(preferences, "{\"Behavior\":\"off\"}");
            else
            {
                var box = (ComboBox)window.FindName("BehaviorBox");
                box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "off");
            }
            var installer = Activator.CreateInstance(installerType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { Path.Combine(scratch, "never-start.exe"), "00" }, null)!;
            completionType.GetMethod("SetResult")!.Invoke(completion, new[] { installer });
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!finished && timer.Elapsed < TimeSpan.FromSeconds(2))
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            }
            if (!finished || starts != 0) throw new InvalidOperationException("Changed update preference still started setup: " + (externalChange ? "external file change" : "selection change"));
            Console.WriteLine("PASS pending automatic download honors " + (externalChange ? "external preference" : "selection change") + " (1 selected regression)");
        }
        finally { window.Close(); File.Delete(preferences); }
    }

    private static void SetBoundary(object options, string name, Func<ParameterExpression[], Expression> body)
    {
        var property = options.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var parameters = property.PropertyType.GetMethod("Invoke")!.GetParameters().Select(value => Expression.Parameter(value.ParameterType)).ToArray();
        property.SetValue(options, Expression.Lambda(property.PropertyType, body(parameters), parameters).Compile());
    }
}
