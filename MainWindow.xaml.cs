using System.Globalization;
using FluentTlsTunnel.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace FluentTlsTunnel;
public sealed partial class MainWindow : Window
{
    Profile profile = Profile.Load();
    Tunnel? tunnel;
    WindowsRouting? routing;
    TrayIcon? tray;
    TrayMenuWindow? trayMenu;
    bool exitRequested;
    CancellationTokenSource? cancel;
    bool loaded, busy, closing, allowClose;
    bool previewConnected;
    Task? connectingTask;
    TaskCompletionSource? connectionFinished;
    readonly System.Diagnostics.Stopwatch connectedClock=new();
    readonly SemaphoreSlim stopping = new(1,1);
    bool Arabic => profile.Language == "ar" || (profile.Language == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar");
    string T(string en,string ar) => Arabic ? ar : en;
    readonly DispatcherTimer heartbeat = new() { Interval=TimeSpan.FromSeconds(1) };
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar=true;AppWindow.TitleBar.PreferredHeightOption=TitleBarHeightOption.Collapsed;SetTitleBar(AppTitleBar);
        AppWindow.Changed+=(_,_)=>UpdateCaptionGlyph();
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow=AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory,"1-AppIcon.ico"));
        var area=DisplayArea.GetFromWindowId(appWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        appWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min(1120,area.Width-60),Math.Min(940,area.Height-60)));
        appWindow.Closing += async (_,e) =>
        {
            if(allowClose) return;
            e.Cancel=true;
            // The close button hides the window to the tray; the tray menu Quit command requests the real exit.
            if(!exitRequested && tray!=null){AppWindow.Hide();return;}
            exitRequested=true;
            if(closing) return;
            closing=true;
            try {if(busy){cancel?.Cancel();if(connectionFinished!=null)await connectionFinished.Task;} await StopAsync(); }
            catch(Exception ex) { Log(ex.Message); closing=false; return; }
            heartbeat.Stop();
            // Arm the final process exit before closing the last UI surface.
            Task.Run(async()=>{await Task.Delay(1500);Environment.Exit(0);});
            tray?.Dispose();trayMenu?.Shutdown();allowClose=true; Close();
        };
        Host.Text=profile.Host; Port.Value=profile.Port; Sni.Text=profile.Sni; Username.Text=profile.Username;
        RouteWindows.IsChecked=profile.RouteWindows; Password.Password=PasswordStore.Load(profile);
        SocksPort.Value=profile.SocksPort; VerifyTls.IsChecked=profile.VerifyTls; LoadDnsFields(profile.DnsServers); UseDns.IsChecked=profile.UseDns;
        WatchMask(); if(profile.MaskValues) ApplyMask();
        foreach(var a in DnsManager.Adapters()) Adapter.Items.Add(a);
        Adapter.SelectedItem=Adapter.Items.Cast<DnsManager.AdapterInfo>().FirstOrDefault(a=>a.Index==profile.AdapterIndex);
        Host.LostFocus+=(_,_)=>RefreshPassword();Username.LostFocus+=(_,_)=>RefreshPassword();Port.LostFocus+=(_,_)=>RefreshPassword();
        Language.SelectedIndex=profile.Language switch {"en"=>1,"ar"=>2,_=>0};
        Theme.SelectedIndex=profile.Theme switch {"light"=>1,"dark"=>2,_=>0};
        (profile.Backdrop switch {"mica"=>MicaOption,"acrylic"=>AcrylicOption,_=>MicaAltOption}).IsChecked=true;
        Nav.SelectedItem=ConnectionNav;
        loaded=true; Localize();
        if(!Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--preview=")))
        {
            trayMenu=new TrayMenuWindow(TrayCommand);
            tray=new TrayIcon(hwnd,Path.Combine(AppContext.BaseDirectory,"1-AppIcon.ico"),Array.Empty<(int,string)>(),()=>ActivateExisting(),TrayCommand,(x,y)=>{trayMenu.ShowAt(x,y,profile.Theme,Arabic,tunnel?.Connected==true,busy);return true;}){NativeTip=true};
            tray.SetTip("Fluent TLS Tunnel");
        }
        if(DnsManager.Pending) { Status.Severity=InfoBarSeverity.Warning; Status.Message=T("A DNS backup is pending. Restore DNS before a new connection.","توجد نسخة احتياطية لإعدادات DNS. استعد الإعدادات قبل اتصال جديد."); }
        Status.RegisterPropertyChangedCallback(InfoBar.MessageProperty,(_,_)=>Status.IsOpen=!string.IsNullOrEmpty(Status.Message));
        heartbeat.Tick += async (_,_) => { UpdateClock(); routing?.PollDiagnostics(); if(!busy && tunnel!=null && (!tunnel.Connected || (routing!=null && !routing.Healthy))) { Log(T("Connection lost.","انقطع الاتصال.")); try { await StopAsync(); } catch(Exception e) { Log(e.Message); } } };
        heartbeat.Start();
        var preview=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--preview="));
        if(preview!=null)
        {
            profile.Theme=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--theme="))?.Split('=')[1]??profile.Theme;
            profile.Backdrop=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--material="))?.Split('=')[1]??profile.Backdrop;
            profile.Language=preview.Split('=')[1]; Language.SelectedIndex=profile.Language=="ar"?2:1;
            var mode=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--state="))?.Split('=')[1];
            previewConnected=mode=="connected";
            if(mode=="settings"||mode=="updates"||mode?.StartsWith("update-")==true) Nav.SelectedItem=SettingsNav;
            if(mode=="about"||mode=="about-updates") Nav.SelectedItem=AboutNav;

            if(previewConnected) {profile.Host="demo.example";connectedClock.Start();}
            loaded=false;(profile.Backdrop switch {"mica"=>MicaOption,"acrylic"=>AcrylicOption,_=>MicaAltOption}).IsChecked=true;loaded=true;
            Localize();
            if(mode?.StartsWith("update-")==true)
            {
                var state=mode.Substring(7);
                string result=state switch {"checking"=>T("Checking...","جارٍ التحقق..."),"current"=>T($"You're up to date (v{CurrentVersion}).",$"أنت على أحدث إصدار (v{CurrentVersion})."),"new"=>T("Version 0.1.3-beta is available.","الإصدار 0.1.3-beta متاح."),"empty"=>T("No releases found yet.","لا توجد إصدارات بعد."),_=>T("Couldn't check for updates. Check your internet connection and try again.","تعذر التحقق من التحديثات. تحقق من اتصالك بالإنترنت وحاول مرة أخرى.")};
                UpdateResultSettings.Text=UpdateResultAbout.Text=result;
                CheckUpdatesSettings.IsEnabled=CheckUpdatesAbout.IsEnabled=state!="checking";
                if(state=="new"){UpdateLinkSettings.Visibility=UpdateLinkAbout.Visibility=Visibility.Visible;}
            }
            if(mode=="updates"||mode?.StartsWith("update-")==true)
            {
                var showUpdates=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};showUpdates.Tick+=(_,_)=>{showUpdates.Stop();SettingsPage.ChangeView(null,double.MaxValue,null);};showUpdates.Start();
            }
            if(mode=="about-updates"||mode=="connection-actions")
            {
                var scroll=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};scroll.Tick+=(_,_)=>{scroll.Stop();if(mode=="about-updates")AboutPanel.ChangeView(null,double.MaxValue,null);else ConnectionPage.ChangeView(null,double.MaxValue,null);};scroll.Start();
            }
            if(mode=="tray")
            {
                trayMenu=new TrayMenuWindow(TrayCommand);
                var showMenu=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};showMenu.Tick+=(_,_)=>{showMenu.Stop();trayMenu.ShowAt(700,650,profile.Theme,Arabic,false,false);trayMenu.AppWindow.Move(new Windows.Graphics.PointInt32(AppWindow.Position.X+AppWindow.Size.Width-250,AppWindow.Position.Y+AppWindow.Size.Height-210));};showMenu.Start();
            }
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    public void ActivateExisting()
    {
        if(allowClose)return;
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Show();
        if(AppWindow.Presenter is OverlappedPresenter p && p.State==OverlappedPresenterState.Minimized)p.Restore();
        // Win32 restore covers a window hidden by the tray close path or minimized behind other windows.
        ShowWindow(handle,IsIconic(handle)?9:5);
        Activate();
        SetForegroundWindow(handle);
    }
    void MinimizeCaptionClick(object sender,RoutedEventArgs e){if(AppWindow.Presenter is OverlappedPresenter p)p.Minimize();}
    void MaximizeCaptionClick(object sender,RoutedEventArgs e){if(AppWindow.Presenter is OverlappedPresenter p){if(p.State==OverlappedPresenterState.Maximized)p.Restore();else p.Maximize();UpdateCaptionGlyph();}}
    void UpdateCaptionGlyph(){if(AppWindow.Presenter is OverlappedPresenter p)MaximizeCaptionGlyph.Glyph=p.State==OverlappedPresenterState.Maximized?"\uE923":"\uE922";}
    void CloseCaptionClick(object sender,RoutedEventArgs e)=>Close();
    void NavChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs e)
    {
        var page=(e.SelectedItem as NavigationViewItem)?.Tag?.ToString()??"connection";
        ConnectionPage.Visibility=page=="connection"?Visibility.Visible:Visibility.Collapsed;
        SettingsPage.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;
        AboutPanel.Visibility=page=="about"?Visibility.Visible:Visibility.Collapsed;
        ConnectionActions.Visibility=page=="connection"?Visibility.Visible:Visibility.Collapsed;
    }
    void Localize()
    {
        Root.FlowDirection=Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MinimizeCaption,T("Minimize","تصغير"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MaximizeCaption,T("Maximize or restore","تكبير أو استعادة"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CloseCaption,T("Close","إغلاق"));
        SystemBackdrop=profile.Backdrop switch {"mica-alt"=>new MicaBackdrop{Kind=Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt},"acrylic"=>new DesktopAcrylicBackdrop(),_=>new MicaBackdrop()};
        Root.RequestedTheme=profile.Theme switch {"light"=>ElementTheme.Light,"dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
        ProfileGroup.Text=T("CONNECTION PROFILE","إعدادات الاتصال");SettingsTitle.Text=T("Settings","الإعدادات");AppearanceGroup.Text=T("APPEARANCE AND LANGUAGE","المظهر واللغة");RoutingGroup.Text=T("ROUTING AND DNS","التوجيه وDNS");
        ConnectionNav.Content=T("Connection","الاتصال"); SettingsNav.Content=T("Settings","الإعدادات"); AboutNav.Content=T("About","حول التطبيق");
        foreach(var heading in new[]{UpdatesHeadingSettings,UpdatesHeadingAbout})heading.Text=T("Updates","التحديثات");
        foreach(var note in new[]{UpdatesNoteSettings,UpdatesNoteAbout})note.Text=T("The app never checks for updates on its own. Press the button to check once, right now.","لا يتحقق التطبيق من التحديثات من تلقاء نفسه. اضغط الزر للتحقق مرة واحدة الآن.");
        foreach(var text in new[]{InstalledVersionSettings,InstalledVersionAbout})text.Text=T("Installed version: v","الإصدار المثبت: v")+CurrentVersion;
        foreach(var button in new[]{CheckUpdatesSettings,CheckUpdatesAbout}){button.Content=T("Check for updates","التحقق من التحديثات");button.HorizontalAlignment=HorizontalAlignment.Left;}
        foreach(var link in new[]{UpdateLinkSettings,UpdateLinkAbout}){link.Content=T("Open download page","فتح صفحة التنزيل");link.HorizontalAlignment=HorizontalAlignment.Left;}
        VersionText.Text="v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        AboutDescription.Text=T("SSH over TLS with SNI, automatic Windows TCP routing and a local SOCKS5 proxy. UDP is not supported.","SSH عبر TLS مع SNI وتوجيه TCP تلقائي لويندوز ووكيل SOCKS5 محلي. لا يُدعم UDP.");
        DeveloperCredit.Text=T("Developed by Mohamed Elnaggar","تطوير محمد النجار");
        RepositoryLink.Content="GitHub Repo Link";
        Language.Header=T("Language","اللغة"); Theme.Header=T("Appearance","المظهر"); BackdropHeading.Text=T("Background","الخلفية");
        Subtitle.Text=T("Connection","الاتصال");
        RouteWindows.Content=T("Route Windows traffic automatically (administrator permission)","توجيه اتصال ويندوز تلقائيًا (يتطلب صلاحية المسؤول)");
        ConnectionTitle.Text=T("Server & authentication","الخادم والمصادقة"); Host.Header=T("Server","الخادم"); Port.Header=T("Port","المنفذ"); Sni.Header="SNI";
        Username.Header=T("SSH username","اسم مستخدم SSH"); Password.Header=T("Password (saved securely)","كلمة المرور (تُحفظ بأمان)");
        VerifyTls.Content=T("Verify TLS certificate (recommended)","التحقق من شهادة TLS (موصى به)");
        TlsHint.Text=T("Some SSH-over-TLS services require this off. SSH host keys are still checked; disabling TLS verification weakens protection.","تتطلب بعض الخدمات تعطيل هذا الخيار. يستمر التحقق من مفتاح SSH، لكن تعطيل التحقق من TLS يقلل الحماية.");
        RoutingTitle.Text=T("Routing & DNS","التوجيه وDNS"); SocksPort.Header=T("Local SOCKS5 port","منفذ SOCKS5 المحلي");
        ProxyHint.Text=T("Automatic mode routes Windows TCP traffic through a virtual adapter. DNS goes through SSH. UDP apps are not supported; Windows VPN badges are not registered. Turn it off to use SOCKS5 manually.","يوجّه الوضع التلقائي اتصال TCP في ويندوز عبر محول افتراضي، وDNS عبر SSH. لا تُدعم تطبيقات UDP، ولا يُسجّل مؤشر VPN الرسمي. عطّله لاستخدام SOCKS5 يدويًا.");
        UseDns.Content=T("Use custom DNS while connected (administrator permission)","استخدام DNS مخصص أثناء الاتصال (يتطلب صلاحية المسؤول)");
        Adapter.Header=T("Network adapter","محول الشبكة"); Dns.Header=T("Primary DNS server IP address","عنوان IP لخادم DNS الأساسي"); DnsSecondary.Header=T("Secondary DNS server IP address (optional)","عنوان IP لخادم DNS الثانوي (اختياري)");
        DnsHint.Text=T("Manual SOCKS mode only. Automatic mode always uses DNS through SSH. Changes the selected Windows adapter, not remote SOCKS DNS. The old settings are backed up and restored on disconnect. After a crash, use Restore DNS. Windows asks for administrator approval each time.","خاص بوضع SOCKS اليدوي. يستخدم الوضع التلقائي DNS عبر SSH. يغيّر محول ويندوز المحدد، لا DNS البعيد في SOCKS. تُحفظ الإعدادات السابقة وتُستعاد عند قطع الاتصال. بعد تعطل التطبيق، اضغط استعادة DNS. يطلب ويندوز موافقة المسؤول لكل تغيير.");
        RestoreRouting.Content=T("Restore Windows routing","استعادة توجيه ويندوز");
        RestoreDns.Content=T("Restore DNS","استعادة DNS"); LogsPanel.Header=T("Connection log","سجل الاتصال"); ClearLog.Content=T("Clear log","مسح السجل");
        Import.Content=T("Import config","استيراد إعدادات"); Export.Content=T("Export config","تصدير إعدادات");
        Save.Content=T("Save profile","حفظ الإعدادات"); SetState();
    }
    void BackdropChanged(object sender,RoutedEventArgs e)
    {
        if(!loaded)return;
        profile.Backdrop=(sender as RadioButton)?.Tag?.ToString()??"mica-alt";
        if(!Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--preview=")))profile.Save();Localize();
    }
    void SettingsChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!loaded) return;
        profile.Language=(Language.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"system";
        profile.Theme=(Theme.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"system";
        if(!Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--preview=")))profile.Save();Localize();
    }
    void SetState()
    {
        bool connected=previewConnected || tunnel?.Connected==true;
        HeroTitle.Text=connected?T("Connected","متصل"):busy?T("Finding your private path…","جارٍ إنشاء مسارك الخاص…"):T("Ready when you are","جاهز عندما تكون مستعدًا");
        HeroDetail.Text=connected?(profile.RouteWindows?T("Windows TCP routing active · DNS through SSH","توجيه TCP في ويندوز نشط · DNS عبر SSH"):$"{profile.Host} · SOCKS5 127.0.0.1:{profile.SocksPort}"):T("Your apps. Your server. One connection.","تطبيقاتك. خادمك. اتصال واحد.");
        ConnectLabel.Text=busy?T("Cancel","إلغاء"):connected?T("Disconnect","قطع الاتصال"):T("Connect","اتصال");
        ConnectIcon.Symbol=connected?Symbol.Accept:busy?Symbol.Cancel:Symbol.Link;
        ConnectionTime.Visibility=connected?Visibility.Visible:Visibility.Collapsed; UpdateClock();
        StatusGlyph.Glyph=connected?"\uE73E":"\uE72E";
        if(connected) StatusGlyph.Foreground=(Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]; else StatusGlyph.ClearValue(FontIcon.ForegroundProperty);
        Status.Severity=connected?InfoBarSeverity.Success:InfoBarSeverity.Informational;
        foreach(var control in new Control[]{Host,Port,Sni,Username,Password,SocksPort,VerifyTls,UseDns,Adapter,Dns,Save,Import,Export,Language,Theme,RestoreDns,RestoreRouting,RouteWindows,DnsSecondary,AcrylicOption,MicaOption,MicaAltOption}) control.IsEnabled=!busy && tunnel==null;
    }
    void LoadDnsFields(string addresses)
    {
        var values=addresses.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        Dns.Text=values.FirstOrDefault()??"";
        // Preserve legacy extra values visibly instead of silently dropping them.
        DnsSecondary.Text=string.Join(", ",values.Skip(1));
    }
    Profile ReadProfile()
    {
        profile.Host=FieldValue(Host); profile.Port=checked((int)Port.Value); profile.Sni=FieldValue(Sni); profile.Username=FieldValue(Username);
        profile.RouteWindows=RouteWindows.IsChecked==true;
        profile.SocksPort=checked((int)SocksPort.Value); profile.VerifyTls=VerifyTls.IsChecked==true; profile.UseDns=UseDns.IsChecked==true;
        var primary=FieldValue(Dns);var secondary=FieldValue(DnsSecondary);
        if((primary.Length>0&&!System.Net.IPAddress.TryParse(primary,out _))||(secondary.Length>0&&!System.Net.IPAddress.TryParse(secondary,out _)))throw new ArgumentException(T("Enter one valid IP address in each DNS field.","أدخل عنوان IP صحيحًا واحدًا في كل خانة DNS."));
        profile.DnsServers=string.Join(", ",new[]{primary,secondary}.Where(v=>v.Length>0)); profile.AdapterIndex=(Adapter.SelectedItem as DnsManager.AdapterInfo)?.Index??0;
        if(profile.RouteWindows)profile.UseDns=false;
        profile.Validate(); return profile;
    }
    const string MaskText="*****";
    readonly Dictionary<TextBox,string> maskedValues=new();
    string FieldValue(TextBox box)=>maskedValues.TryGetValue(box,out var real)&&box.Text==MaskText?real:box.Text.Trim();
    void ApplyMask()
    {
        foreach(var box in new[]{Host,Sni,Username,Dns,DnsSecondary})
        {
            var text=box.Text.Trim(); if(text.Length==0||text==MaskText) continue;
            maskedValues[box]=text; box.Text=MaskText;
        }
    }
    void WatchMask()
    {
        // Typing into a masked field replaces its hidden value; untouched masked fields keep their real value.
        foreach(var box in new[]{Host,Sni,Username,Dns,DnsSecondary}) box.TextChanged+=(_,_)=>{if(box.Text!=MaskText) maskedValues.Remove(box);};
    }
    void ClearLogClick(object sender,RoutedEventArgs e) => Logs.Text="";
    void Log(string message) => DispatcherQueue.TryEnqueue(()=>
    {
        if(message=="tls-ready") message=T("TLS ready.","اتصال TLS جاهز.");
        if(message=="connected") message=T("SSH ready. Local SOCKS5 is listening.","اتصال SSH جاهز. يعمل SOCKS5 المحلي.");
        Logs.Text=($"{DateTime.Now:HH:mm:ss} {message}\n"+Logs.Text);
        if(Logs.Text.Length>12000) Logs.Text=Logs.Text[..12000];
    });
    Task<bool> ConfirmKey(string endpoint,string key,bool changed)
    {
        var result=new TaskCompletionSource<bool>();
        DispatcherQueue.TryEnqueue(async ()=>
        {
            try
            {
                var dialog=new ContentDialog { XamlRoot=Root.XamlRoot, Title=changed?T("SSH host key changed","تغيّر مفتاح خادم SSH"):T("Trust this SSH server?","هل تثق بخادم SSH؟"),
                    Content=endpoint+"\n\n"+key+"\n\n"+T("Compare this fingerprint with your provider before accepting. A changed key may mean an attack or a replaced server.","تحقق من هذه البصمة لدى مزود الخدمة قبل الموافقة. قد يدل تغير المفتاح على هجوم أو استبدال الخادم."),
                    PrimaryButtonText=T("Trust key","الثقة بالمفتاح"), CloseButtonText=T("Cancel","إلغاء"), DefaultButton=ContentDialogButton.Close };
                result.TrySetResult(await dialog.ShowAsync()==ContentDialogResult.Primary);
            } catch { result.TrySetResult(false); }
        });
        return result.Task;
    }
    async void ConnectClick(object sender,RoutedEventArgs e)
    {
        if(busy) { cancel?.Cancel(); return; }
        if(tunnel!=null) { try { await StopAsync(); } catch(Exception ex) { Status.Message=ex.Message; } return; }
        try
        {
            var p=ReadProfile();
            if(DnsManager.Pending) throw new InvalidOperationException(T("Restore the pending DNS backup first.","استعد إعدادات DNS السابقة أولًا."));
            if(Password.Password.Length==0) throw new ArgumentException(T("Enter your password.","أدخل كلمة المرور."));
            try { PasswordStore.Save(p,Password.Password); }
            catch { Log(T("Password could not be saved securely. Connecting without saving it.","تعذر حفظ كلمة المرور بأمان. سيستمر الاتصال دون حفظها.")); }
            p.Save();connectionFinished=new(TaskCreationOptions.RunContinuationsAsynchronously); busy=true; Status.Message=""; cancel=new(); tunnel=new(); SetState();
            var password = Password.Password;
            connectingTask=Task.Run(()=>tunnel.ConnectAsync(p,password,ConfirmKey,Log,cancel.Token));
            await connectingTask;
            cancel.Token.ThrowIfCancellationRequested();
            if(p.RouteWindows){routing=new();await routing.StartAsync(tunnel.RemoteAddress??throw new InvalidOperationException("TLS endpoint unavailable"),p.SocksPort,cancel.Token,tunnel.LocalAddress,Log,Profile.ParseDns(p.DnsServers));}
            if(p.UseDns && !p.RouteWindows) await DnsManager.ApplyAsync(p.AdapterIndex,Profile.ParseDns(p.DnsServers));
            connectedClock.Restart(); ShowConnectedNotification();

            Status.Message=p.RouteWindows?T("Windows TCP routing is active. UDP is not supported.","توجيه TCP في ويندوز نشط. لا يُدعم UDP."):$"SOCKS5 · 127.0.0.1:{p.SocksPort}";
        }
        catch(Exception ex) { Log(ex.Message); try { await StopAsync(); } catch(Exception restore) { Log(restore.Message); } Status.Message=ex.Message; }
        finally { busy=false; connectingTask=null; SetState();connectionFinished?.TrySetResult(); }
    }
    async Task StopAsync()
    {
        cancel?.Cancel();
        if(connectingTask!=null) try { await connectingTask; } catch { }
        await stopping.WaitAsync();
        try
        {
            if(routing!=null){await routing.DisposeAsync();routing=null;}
            if(tunnel!=null) { await Task.Run(async ()=>await tunnel.DisposeAsync()); tunnel=null; }
            if(DnsManager.Pending) await DnsManager.RestoreAsync();
            cancel?.Dispose(); cancel=null; connectedClock.Reset(); SetState();
        }
        finally { stopping.Release(); }
    }
    void SaveClick(object sender,RoutedEventArgs e) { try { var p=ReadProfile();PasswordStore.Save(p,Password.Password);p.Save(); Status.Message=T("Profile and password saved securely.","حُفظت الإعدادات وكلمة المرور بأمان."); } catch(Exception ex) { Status.Message=ex.Message; } }
    async void RestoreRoutingClick(object sender,RoutedEventArgs e){try{await WindowsRouting.RestoreAsync();Status.Message=T("Windows routing restored.","استُعيد توجيه ويندوز.");}catch(Exception ex){Status.Message=ex.Message;}}
    async void RestoreDnsClick(object sender,RoutedEventArgs e) { try { await DnsManager.RestoreAsync(); Status.Message=T("Previous DNS settings restored.","استُعيدت إعدادات DNS السابقة."); } catch(Exception ex) { Status.Message=ex.Message; } }
    async void ExportClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var p=ReadProfile();
            var picker=new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName="fluent-tls-config" };
            picker.FileTypeChoices.Add("Tunnel configuration",new List<string>{".ftlsc"});
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file=await picker.PickSaveFileAsync(); if(file==null) return;
            // Export network settings only. Never export passwords or trust records.
            var portable=new Profile { Host=p.Host,Port=p.Port,Sni=p.Sni,Username=p.Username,SocksPort=p.SocksPort,VerifyTls=p.VerifyTls,DnsServers=p.DnsServers };
            var protection=await ExportProtection(); if(!protection.Proceed) return;
            await Windows.Storage.FileIO.WriteTextAsync(file,ConfigCrypto.Encode(portable,protection.Password));
            Status.Message=T("Config exported without passwords or trusted keys.","صُدّرت الإعدادات دون كلمات مرور أو مفاتيح موثوقة.");
        } catch(Exception ex) { Status.Message=ex.Message; }
    }
    async void ImportClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var picker=new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".ftlsc");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file=await picker.PickSingleFileAsync(); if(file==null) return;
            if((await file.GetBasicPropertiesAsync()).Size>65536) throw new ArgumentException(T("Config file is too large.","ملف الإعدادات كبير جدًا."));
            var content=await Windows.Storage.FileIO.ReadTextAsync(file);
            string? secret=null; bool maskValues=ConfigCrypto.Encrypted(content); if(maskValues) {var answer=await ImportPassword(); if(answer==null) return; secret=answer;}
            var p=ConfigCrypto.Decode(content,secret);
            p.UseDns=false; p.AdapterIndex=0; p.RouteWindows=false; p.Validate(); Profile.ParseDns(p.DnsServers);
            var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=T("Import these settings?","هل تريد استيراد هذه الإعدادات؟"),Content=maskValues?"*****:*****\nSNI: *****\nSSH: *****\nSOCKS: *****\nDNS: *****":$"{p.Host}:{p.Port}\nSNI: {p.Sni}\nSSH: {p.Username}\nSOCKS: {p.SocksPort}\nDNS: {p.DnsServers}",PrimaryButtonText=T("Import","استيراد"),CloseButtonText=T("Cancel","إلغاء"),DefaultButton=ContentDialogButton.Close};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary) return;
            Host.Text=p.Host;Port.Value=p.Port;Sni.Text=p.Sni;Username.Text=p.Username;SocksPort.Value=p.SocksPort;VerifyTls.IsChecked=p.VerifyTls;LoadDnsFields(p.DnsServers);UseDns.IsChecked=false;
            maskedValues.Clear(); profile.MaskValues=maskValues; if(maskValues) ApplyMask();

            Status.Message=T("Config imported. Review it before connecting. DNS changes remain off.","استُوردت الإعدادات. راجعها قبل الاتصال. يبقى تغيير DNS معطّلًا.");
        } catch(Exception ex) { Status.Message=ex.Message; }
    }

    async Task<(bool Proceed,string? Password)> ExportProtection()
    {
        var check=new CheckBox {Content=T("Encrypt with a password","التشفير بكلمة مرور")};
        var secret=new PasswordBox {Header=T("Export password (at least 8 characters)","كلمة مرور التصدير (8 أحرف على الأقل)")};
        var stack=new StackPanel {Spacing=12};stack.Children.Add(check);stack.Children.Add(secret);
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=T("Protect this config","حماية ملف الإعدادات"),Content=stack,PrimaryButtonText=T("Export","تصدير"),CloseButtonText=T("Cancel","إلغاء")};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary) return (false,null);
        if(check.IsChecked==true && secret.Password.Length<8) throw new ArgumentException(T("Use at least 8 characters for the export password.","استخدم 8 أحرف على الأقل لكلمة مرور التصدير."));
        return (true,check.IsChecked==true?secret.Password:null);
    }
    async Task<string?> ImportPassword()
    {
        var secret=new PasswordBox {Header=T("Config password","كلمة مرور ملف الإعدادات")};
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=T("Encrypted config","ملف إعدادات مشفّر"),Content=secret,PrimaryButtonText=T("Unlock","فتح"),CloseButtonText=T("Cancel","إلغاء")};
        return await dialog.ShowAsync()==ContentDialogResult.Primary?secret.Password:null;
    }

    static string CurrentVersion=>typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??"0.1.2";
    bool checkingUpdate;
    async void CheckUpdatesClick(object sender,RoutedEventArgs e)
    {
        if(checkingUpdate)return;checkingUpdate=true;
        CheckUpdatesSettings.IsEnabled=CheckUpdatesAbout.IsEnabled=false;
        UpdateLinkSettings.Visibility=UpdateLinkAbout.Visibility=Visibility.Collapsed;
        UpdateResultSettings.Text=UpdateResultAbout.Text=T("Checking...","جارٍ التحقق...");
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentTlsTunnel/"+CurrentVersion);
            using var response=await http.GetAsync("https://api.github.com/repos/Star-Dust-0/fluent-tls-tunnel/releases?per_page=30");response.EnsureSuccessStatusCode();
            using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Version? newest=null;Uri? url=null;string newestTag="";
            foreach(var r in json.RootElement.EnumerateArray())
            {
                if(r.TryGetProperty("draft",out var draft)&&draft.GetBoolean())continue;
                var tag=(r.GetProperty("tag_name").GetString()??"").TrimStart('v','V');
                if(Version.TryParse(tag.Split('-','+')[0],out var v)&&(newest==null||v>newest)&&Uri.TryCreate(r.GetProperty("html_url").GetString(),UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Host=="github.com"&&u.AbsolutePath.StartsWith("/Star-Dust-0/fluent-tls-tunnel/releases/",StringComparison.OrdinalIgnoreCase)){newest=v;url=u;newestTag=tag;}
            }
            string result;
            if(newest==null)result=T("No releases found yet.","لا توجد إصدارات بعد.");
            else if(newest>Version.Parse(CurrentVersion))
            {
                result=T($"Version {newestTag} is available.",$"الإصدار {newestTag} متاح.");
                UpdateLinkSettings.NavigateUri=UpdateLinkAbout.NavigateUri=url;
                UpdateLinkSettings.Visibility=UpdateLinkAbout.Visibility=Visibility.Visible;
            }
            else result=T($"You're up to date (v{CurrentVersion}).",$"أنت على أحدث إصدار (v{CurrentVersion}).");
            UpdateResultSettings.Text=UpdateResultAbout.Text=result;
        }
        catch{UpdateResultSettings.Text=UpdateResultAbout.Text=T("Couldn't check for updates. Check your internet connection and try again.","تعذر التحقق من التحديثات. تحقق من اتصالك بالإنترنت وحاول مرة أخرى.");}
        finally{checkingUpdate=false;CheckUpdatesSettings.IsEnabled=CheckUpdatesAbout.IsEnabled=true;}
    }
    string passwordIdentity="";
    void RefreshPassword()
    {
        var p=new Profile{Host=FieldValue(Host),Port=(int)Port.Value,Username=FieldValue(Username)};
        var key=p.Endpoint+"/"+p.Username;
        if(passwordIdentity.Length==0)passwordIdentity=profile.Endpoint+"/"+profile.Username;
        if(key!=passwordIdentity){Password.Password=PasswordStore.Load(p);passwordIdentity=key;}
    }
    void TrayCommand(int command)
    {
        if(command==4){exitRequested=true;Close();return;}
        AppWindow.Show();Activate();
        if(command==2)Nav.SelectedItem=SettingsNav;
        else if(command==3)Nav.SelectedItem=AboutNav;
        else {Nav.SelectedItem=ConnectionNav;ConnectClick(ConnectButton,new RoutedEventArgs());}
    }
    void UpdateClock()
    {
        var elapsed=connectedClock.Elapsed;
        ConnectionTime.Text=$"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
    void ShowConnectedNotification()
    {
        try
        {
            var notification=new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                .AddText("Fluent TLS Tunnel").AddText(T("Connected","متصل")).BuildNotification();
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
        }
        catch { Log(T("Windows notifications are unavailable.","إشعارات ويندوز غير متاحة.")); }
    }

}
