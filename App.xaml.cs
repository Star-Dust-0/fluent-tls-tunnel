using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
namespace FluentTlsTunnel;
public partial class App : Application
{
    MainWindow? window;
    Mutex? instance;
    EventWaitHandle? activation;
    RegisteredWaitHandle? activationWait;
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Both objects are local to this Windows session. The second launch only requests activation.
        activation=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\FluentTlsTunnel.Activate");
        instance=new Mutex(false,"Local\\FluentTlsTunnel.SingleInstance");
        bool owns;
        try { owns=instance.WaitOne(0); } catch(AbandonedMutexException) { owns=true; }
        if(!owns)
        {
            AllowSetForegroundWindow(uint.MaxValue);
            activation.Set();
            activation.Dispose();instance.Dispose();
            Environment.Exit(0);return;
        }
        window=new MainWindow();
        activationWait=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>
            window.DispatcherQueue.TryEnqueue(()=>window.ActivateExisting()),null,Timeout.Infinite,false);
        try
        {
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.NotificationInvoked += (_,_) => window.DispatcherQueue.TryEnqueue(()=>window.ActivateExisting());
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
        } catch { }
        window.ActivateExisting();
    }
}
