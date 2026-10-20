using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace AintoWindows;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xA17;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private readonly DispatcherTimer clipboardTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly ObservableCollection<LaunchItem> results = [];
    private readonly ObservableCollection<ClipItem> clips = [];
    private readonly ObservableCollection<SnippetItem> snippets = [];
    private readonly List<ClipItem> allClips = [];
    private readonly List<SnippetItem> allSnippets = [];
    private readonly Dictionary<string,ImageSource?> iconCache = new(StringComparer.OrdinalIgnoreCase);
    private List<ApplicationEntry> applications = [];
    private bool appIndexReady;
    private CancellationTokenSource? iconLoadCancellation;
    private readonly DispatcherTimer searchDebounceTimer = new(){Interval=TimeSpan.FromMilliseconds(55)};
    private string lastClipboard = "";
    private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AintoWindows");
    private string clipFile = "";
    private string snippetFile = "";
    private string themeFile = "";
    private string startupFile = "";
    private string themeMode = "Dark";
    private string shortcutModifiers="Alt";
    private string shortcutKey="Space";
    private string settingsFile = "";
    private int panelRadius = 16;
    private int panelOpacity = 92;
    private int acrylicAmount = 48;
    private int panelWidth = 420;
    private int cornerRadius = 10;
    private int shadowAmount = 60;
    private int glassIntensity = 76;
    private bool acrylicEnabled = true;
    private bool showFooter = true;
    private HwndSource? source;
    private int activeMode;
    private IntPtr previousForegroundWindow;
    private bool transitioning;
    private bool settingsOpen;
    private bool glassPointerMoved;
    private double lastPanelHeight = 58;
    private DispatcherTimer? smoothScrollTimer;
    private double smoothScrollStartOffset;
    private double smoothScrollTargetOffset;
    private long smoothScrollStartTimestamp;
    private System.Windows.Forms.NotifyIcon? trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        // app.Run(window) briefly shows its startup window; keep that bootstrap
        // frame invisible so users only see the launcher when they invoke it.
        Opacity=0;
        LogoImage.Source=AppAssets.LoadLogo();
        Tag=new CornerRadius(cornerRadius);
        SizeChanged+=(_,_)=>{ApplyWindowRegion();UpdateGlassLayout();};
        Results.ItemsSource = results; ClipboardItems.ItemsSource = clips; SnippetItems.ItemsSource = snippets;
        Directory.CreateDirectory(dataDir); clipFile = Path.Combine(dataDir, "clipboard.json"); snippetFile = Path.Combine(dataDir, "snippets.json"); themeFile=Path.Combine(dataDir,"theme.json"); startupFile=Path.Combine(dataDir,"startup.json");settingsFile=Path.Combine(dataDir,"settings.json");
        LoadTheme();LoadSettings();Width=panelWidth;ApplyTheme(); LoadSnippets(); LoadClips(); RefreshResults();
        searchDebounceTimer.Tick+=(_,_)=>{searchDebounceTimer.Stop();RefreshCurrentMode();};
        _=RefreshAppsAsync();
        InitializeTray();
        SourceInitialized += OnSourceInitialized; Loaded += (_, _) => { Hide(); Opacity=1; QueryBox.Focus(); QueryBox.SelectAll(); };
        clipboardTimer.Tick += (_, _) => CaptureClipboard(); clipboardTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(handle); source.AddHook(WndProc);
        ApplyWindowRegion();
        ApplyAcrylic();
        if (!TryRegisterLauncherShortcut(shortcutModifiers,shortcutKey)) { EmptyText.Text = "Launcher shortcut is unavailable. Change it in Settings."; EmptyText.Visibility = Visibility.Visible; }
    }
    private void InitializeTray()
    {
        var menu=new System.Windows.Forms.ContextMenuStrip();
        var openItem=new System.Windows.Forms.ToolStripMenuItem("Open Style Search");
        openItem.Click+=(_,_)=>Dispatcher.Invoke(ToggleLauncher);
        var startupEnabled=true;
        try{if(File.Exists(startupFile))startupEnabled=JsonSerializer.Deserialize<bool>(File.ReadAllText(startupFile));}catch{}
        var startupItem=new System.Windows.Forms.ToolStripMenuItem("Run at sign-in"){Checked=startupEnabled,CheckOnClick=true};
        startupItem.CheckedChanged+=(_,_)=>SetRunAtSignIn(startupItem.Checked);
        var settingsItem=new System.Windows.Forms.ToolStripMenuItem("Settings…");
        settingsItem.Click+=(_,_)=>Dispatcher.Invoke(OpenSettings);
        var exitItem=new System.Windows.Forms.ToolStripMenuItem("Exit Style Search");
        exitItem.Click+=(_,_)=>Dispatcher.Invoke(()=>{Close();System.Windows.Application.Current.Shutdown();});
        menu.Items.Add(openItem);menu.Items.Add(settingsItem);menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());menu.Items.Add(startupItem);menu.Items.Add(exitItem);
        trayIcon=new System.Windows.Forms.NotifyIcon{Icon=LoadTrayIcon(),Text="Style Search",ContextMenuStrip=menu,Visible=true};
        trayIcon.DoubleClick+=(_,_)=>Dispatcher.Invoke(ToggleLauncher);
        SetRunAtSignIn(startupEnabled);
    }
    private System.Drawing.Icon LoadTrayIcon()=>AppAssets.LoadTrayIcon();
    private void SetRunAtSignIn(bool enabled)
    {
        try
        {
            using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if(enabled){var exe=Process.GetCurrentProcess().MainModule?.FileName;if(!string.IsNullOrWhiteSpace(exe))key?.SetValue("StyleSearch",$"\"{exe}\"");}
            else key?.DeleteValue("StyleSearch",false);
            key?.DeleteValue("AintoWindows",false);
            File.WriteAllText(startupFile,JsonSerializer.Serialize(enabled));
        }
        catch { }
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId) { if(!settingsOpen)ToggleLauncher(); handled = true; }
        return IntPtr.Zero;
    }
    private void ToggleLauncher()
    {
        if(transitioning)return;
        if (IsVisible) { HideAnimated(); return; }
        previousForegroundWindow=GetForegroundWindow();
        searchDebounceTimer.Stop();activeMode=0; UpdateModeVisibility(); QueryBox.Clear(); RefreshResults();
        Left=(SystemParameters.WorkArea.Width-Width)/2;Top=SystemParameters.WorkArea.Top+SystemParameters.WorkArea.Height*0.25;
        BeginAnimation(OpacityProperty,null);Opacity=1;
        BeginAnimation(HeightProperty,null);
        Height=50;
        Show(); Activate(); Topmost=true; QueryBox.Focus();
        transitioning=true;
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        var showAnimation=new DoubleAnimation(50,58,TimeSpan.FromMilliseconds(190))
        {EasingFunction=ease,FillBehavior=FillBehavior.Stop};
        showAnimation.Completed+=(_,_)=>{Height=58;transitioning=false;};
        BeginAnimation(HeightProperty,showAnimation,HandoffBehavior.SnapshotAndReplace);
    }
    private void HideAnimated()
    {
        if(!IsVisible||transitioning)return;
        BeginAnimation(HeightProperty,null);
        Hide();
    }
    private void RefreshApps()
    {
        _=RefreshAppsAsync();
    }
    private async Task RefreshAppsAsync()
    {
        var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)};
        try
        {
            var found=await Task.Run(()=>
            {
                var entries=new Dictionary<string,ApplicationEntry>(StringComparer.OrdinalIgnoreCase);
                foreach(var root in roots)
                {
                    if(!Directory.Exists(root))continue;
                    try
                    {
                        foreach(var path in Directory.EnumerateFiles(root,"*.lnk",SearchOption.AllDirectories))
                        {
                            var name=Path.GetFileNameWithoutExtension(path);
                            if(!string.IsNullOrWhiteSpace(name))entries.TryAdd(path,new ApplicationEntry(name,path));
                        }
                    }
                    catch(IOException){}catch(UnauthorizedAccessException){}
                }
                return entries.Values.OrderBy(entry=>entry.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
            });
            applications=found;
            appIndexReady=true;
            if(IsVisible&&activeMode==0)RefreshResults();
        }
        catch{appIndexReady=true;}
    }
    private void RefreshResults()
    {
        var q=QueryBox.Text.Trim();results.Clear();
        iconLoadCancellation?.Cancel();iconLoadCancellation?.Dispose();iconLoadCancellation=null;
        var entries=q.Length==0?Enumerable.Empty<ApplicationEntry>():applications
            .Select(entry=>(Entry:entry,Score:MatchScore(entry.Name,q)))
            .Where(match=>match.Score>0).OrderByDescending(match=>match.Score)
            .ThenBy(match=>match.Entry.Name,StringComparer.CurrentCultureIgnoreCase)
            .Take(40).Select(match=>match.Entry).ToList();
        var iconsToLoad=new List<LaunchItem>();
        foreach(var entry in entries)
        {
            iconCache.TryGetValue(entry.Path,out var icon);
            var item=new LaunchItem(entry.Name,entry.Path,"Application",icon);
            results.Add(item);
            if(!iconCache.ContainsKey(entry.Path))iconsToLoad.Add(item);
        }
        if (q.Length > 1) results.Add(new LaunchItem($"Search the web for ‘{q}’", "https://www.google.com/search?q="+Uri.EscapeDataString(q), "Web search", null));
        if (results.Count > 0) Results.SelectedIndex = 0;
        EmptyText.Text=results.Count==0?(!appIndexReady&&q.Length>0?"Loading apps…":"No results"):"";EmptyText.Visibility=results.Count==0?Visibility.Visible:Visibility.Collapsed;
        FooterStatus.Text=string.IsNullOrWhiteSpace(q)?"":$"{results.Count} results";
        FooterHints.Text="↑ ↓ navigate     ↵ open     esc clear";
        var expanded=q.Length>0;
        ContentArea.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        HeaderRule.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        FooterPanel.Visibility=expanded&&showFooter?Visibility.Visible:Visibility.Collapsed;
        FooterRule.Visibility=expanded&&showFooter?Visibility.Visible:Visibility.Collapsed;
        SetPanelHeight();UpdateWatermark();
        if(iconsToLoad.Count>0&&q.Length>0)
        {
            iconLoadCancellation=new CancellationTokenSource();
            _=LoadResultIconsAsync(iconsToLoad,iconLoadCancellation.Token);
        }
    }
    private static int MatchScore(string name, string query)
    {
        if (name.Contains(query,StringComparison.OrdinalIgnoreCase)) return 100-(name.Length-query.Length);
        var ni=0; var streak=0; var best=0; var score=0;
        foreach(var ch in name){ if(ni<query.Length&&char.ToLowerInvariant(ch)==char.ToLowerInvariant(query[ni])){ni++;streak++;score+=10+streak*3;best=Math.Max(best,streak);}else streak=0; }
        return ni==query.Length ? score+best*3-name.Length : 0;
    }
    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Results is null) return;
        searchDebounceTimer.Stop();
        searchDebounceTimer.Start();
    }
    private async Task LoadResultIconsAsync(IReadOnlyList<LaunchItem> items,CancellationToken token)
    {
        using var gate=new SemaphoreSlim(4,4);
        var tasks=items.Select(async item=>
        {
            var entered=false;
            try
            {
                await gate.WaitAsync(token);entered=true;
                var pixels=await Task.Run(()=>GetShellIconPixels(item.Path),token);
                token.ThrowIfCancellationRequested();
                await Dispatcher.InvokeAsync(() =>
                {
                    if(token.IsCancellationRequested)return;
                    ImageSource? image=null;
                    if(pixels is not null)
                    {
                        var bitmap=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,pixels,32*4);
                        bitmap.Freeze();image=bitmap;
                    }
                    iconCache[item.Path]=image;
                    item.Icon=image;
                });
            }
            catch(OperationCanceledException){}
            catch{}
            finally{if(entered)gate.Release();}
        });
        await Task.WhenAll(tasks);
    }

    private static byte[]? GetShellIconPixels(string path)
    {
        SHFILEINFO info=new();
        var handle=SHGetFileInfo(path,0,ref info,(uint)Marshal.SizeOf<SHFILEINFO>(),0x100|0x1);
        if(handle==IntPtr.Zero||info.hIcon==IntPtr.Zero)return null;
        try
        {
            using var source=System.Drawing.Icon.FromHandle(info.hIcon).ToBitmap();
            using var bitmap=new System.Drawing.Bitmap(32,32,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.DrawImage(source,0,0,32,32);
            }
            var rectangle=new System.Drawing.Rectangle(0,0,32,32);
            var data=bitmap.LockBits(rectangle,System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var pixels=new byte[32*32*4];
                for(var row=0;row<32;row++)Marshal.Copy(IntPtr.Add(data.Scan0,row*data.Stride),pixels,row*32*4,32*4);
                return pixels;
            }
            finally{bitmap.UnlockBits(data);}
        }
        finally{DestroyIcon(info.hIcon);}
    }
    private void Results_PreviewMouseWheel(object sender,MouseWheelEventArgs e)
    {
        var viewer=FindVisualChild<ScrollViewer>(Results);
        if(viewer is null||viewer.ScrollableHeight<=0)return;
        e.Handled=true;
        smoothScrollStartOffset=viewer.VerticalOffset;
        smoothScrollTargetOffset=Math.Clamp(viewer.VerticalOffset-e.Delta*0.65,0,viewer.ScrollableHeight);
        smoothScrollStartTimestamp=Stopwatch.GetTimestamp();
        if(smoothScrollTimer is null)
        {
            smoothScrollTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
            smoothScrollTimer.Tick+=(_,_)=>
            {
                var scrollViewer=FindVisualChild<ScrollViewer>(Results);
                if(scrollViewer is null){smoothScrollTimer.Stop();return;}
                var t=Math.Clamp(Stopwatch.GetElapsedTime(smoothScrollStartTimestamp).TotalMilliseconds/190.0,0,1);
                var eased=1-Math.Pow(1-t,3);
                scrollViewer.ScrollToVerticalOffset(smoothScrollStartOffset+(smoothScrollTargetOffset-smoothScrollStartOffset)*eased);
                if(t>=1)smoothScrollTimer.Stop();
            };
        }
        smoothScrollTimer.Stop();
        smoothScrollTimer.Start();
    }
    private static T? FindVisualChild<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)return match;
            var nested=FindVisualChild<T>(child);
            if(nested is not null)return nested;
        }
        return null;
    }
    private void RefreshCurrentMode(){if(activeMode==0)RefreshResults();else if(activeMode==1)RefreshClips();else RefreshSnippets();}
    private void UpdateModeVisibility()
    {
        Results.Visibility=activeMode==0?Visibility.Visible:Visibility.Collapsed;
        ClipboardPage.Visibility=activeMode==1?Visibility.Visible:Visibility.Collapsed;
        SnippetPage.Visibility=activeMode==2?Visibility.Visible:Visibility.Collapsed;
        BackButton.Visibility=activeMode==0?Visibility.Collapsed:Visibility.Visible;
        ModeGlyph.Text=activeMode==0?"⌕":activeMode==1?"▤":"❝";
        LogoImage.Visibility=activeMode==0?Visibility.Visible:Visibility.Collapsed;
        ModeGlyph.Visibility=activeMode==0?Visibility.Collapsed:Visibility.Visible;
        QueryBox.ToolTip=null;
        UpdateWatermark();SetPanelHeight();
    }
    private void UpdateWatermark(){if(Watermark is null||QueryBox is null)return;Watermark.Text=activeMode==0?"Search...":activeMode==1?"Type to filter entries...":"Filter snippets...";Watermark.Visibility=QueryBox.Text.Length==0?Visibility.Visible:Visibility.Collapsed;}
    private void SetPanelHeight()
    {
        var q=QueryBox?.Text??"";
        var target=activeMode==0&&q.Length==0?58d:
            Math.Clamp(58+1+(activeMode==0?Math.Min(400,Math.Max(0,results.Count*43)):360)+(showFooter?34:0),92,540);
        Width=panelWidth;
        if(Math.Abs(target-lastPanelHeight)<0.5)return;
        lastPanelHeight=target;
        if(!IsVisible||ActualHeight<=0)
        {
            BeginAnimation(HeightProperty,null);
            Height=target;
            return;
        }
        var from=Math.Max(58,ActualHeight);
        var animation=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(210))
        {
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},
            FillBehavior=FillBehavior.Stop
        };
        animation.Completed+=(_,_)=>Height=target;
        BeginAnimation(HeightProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    private void LoadTheme()
    {
        try { if(File.Exists(themeFile)) themeMode=JsonSerializer.Deserialize<string>(File.ReadAllText(themeFile))??"Dark"; }
        catch { themeMode="Dark"; }
        if(themeMode is not ("System" or "Light" or "Dark"))themeMode="Dark";
    }
    private void SaveTheme(){try{File.WriteAllText(themeFile,JsonSerializer.Serialize(themeMode));}catch{}}
    private void LoadSettings(){try{if(File.Exists(settingsFile)){var s=JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(settingsFile));if(s is not null){panelRadius=Math.Clamp(s.PanelRadius,0,36);panelOpacity=Math.Clamp(s.PanelOpacity,20,100);acrylicAmount=Math.Clamp(s.AcrylicAmount,0,100);panelWidth=Math.Clamp(s.PanelWidth,360,900);cornerRadius=Math.Clamp(s.RowRadius,0,24);shadowAmount=Math.Clamp(s.Shadow,0,100);glassIntensity=Math.Clamp(s.GlassIntensity==0?76:s.GlassIntensity,0,100);acrylicEnabled=s.Acrylic;showFooter=s.ShowFooter;shortcutModifiers=string.IsNullOrWhiteSpace(s.ShortcutModifiers)?"Alt":s.ShortcutModifiers;shortcutKey=string.IsNullOrWhiteSpace(s.ShortcutKey)?"Space":s.ShortcutKey;}}}catch{}}
    private void SaveSettings(){try{File.WriteAllText(settingsFile,JsonSerializer.Serialize(new LauncherSettings(panelRadius,panelOpacity,acrylicAmount,panelWidth,cornerRadius,shadowAmount,acrylicEnabled,showFooter,glassIntensity,shortcutModifiers,shortcutKey)));}catch{}}
    private void ApplyTheme()
    {
        var isDark=IsDarkTheme();
        var palette=isDark?new Dictionary<string,string>
        {
            ["Panel"]="#38111820",["PanelBorder"]="#3DFFFFFF",["Rule"]="#30FFFFFF",["Ink"]="#FFF9FAFC",["SecondaryInk"]="#FFAEB3BC",
            ["IconInk"]="#FFE6E8EC",["Tile"]="#14000000",["TileBorder"]="#28FFFFFF",["TileInk"]="#FFE0E4EB",["SelectedRow"]="#3AFFFFFF",["HoverRow"]="#20FFFFFF",["EmptyInk"]="#FFAEB3BC"
        }:new Dictionary<string,string>
        {
            ["Panel"]="#38DCE6F2",["PanelBorder"]="#90FFFFFF",["Rule"]="#65FFFFFF",["Ink"]="#202124",["SecondaryInk"]="#656B75",
            ["IconInk"]="#53565B",["Tile"]="#DFFFFFFF",["TileBorder"]="#70FFFFFF",["TileInk"]="#56616E",["SelectedRow"]="#668A9CB6",["HoverRow"]="#44A6B5C6",["EmptyInk"]="#727984"
        };
        if(acrylicEnabled)
        {
            // Keep the desktop-acrylic base tint dark in Dark mode so rounded edges
            // never reveal the bright system backdrop as a white fringe.
            var alpha=(byte)Math.Clamp((100-panelOpacity)*0.55+(100-acrylicAmount)*0.18,isDark?5:8,96);
            palette["Panel"]=$"#{alpha:X2}11151C";
        }
        foreach(var pair in palette)Resources[pair.Key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value)!);
        // Keep the WPF surface translucent so DWM's live system backdrop shows through.
        Panel.Background=System.Windows.Media.Brushes.Transparent;
        Panel.CornerRadius=new CornerRadius(panelRadius);
        Tag=new CornerRadius(cornerRadius);
        // Keep the glass surface flush to the search bar. The former outside shadow
        // expanded as a dark rectangular halo around the rounded panel.
        Panel.Effect=null;
        Panel.Opacity=1;
        UpdateLiquidGlass(isDark);
        ApplyAcrylic();
        ApplyWindowRegion();
        ToolTip= $"Appearance: {themeMode}";
    }
    private bool IsDarkTheme()=>themeMode=="Dark"||(themeMode=="System"&&!SystemPrefersLight());
    private void UpdateLiquidGlass(bool dark)
    {
        if(GlassOverlay is null)return;
        GlassOverlay.Opacity=acrylicEnabled?glassIntensity/100.0*0.82:0;
        GlassTint.CornerRadius=new CornerRadius(panelRadius);
        GlassInnerRim.Opacity=shadowAmount/100.0*0.35;
        var rimColor=dark?Color.FromArgb(66,244,250,255):Color.FromArgb(75,255,255,255);
        var coolRim=dark?Color.FromArgb(31,116,203,255):Color.FromArgb(48,94,139,185);
        var rimBrush=new LinearGradientBrush{StartPoint=new System.Windows.Point(0.08,0),EndPoint=new System.Windows.Point(0.92,1)};
        rimBrush.GradientStops.Add(new GradientStop(rimColor,0));
        rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(12,rimColor.R,rimColor.G,rimColor.B),0.32));
        rimBrush.GradientStops.Add(new GradientStop(coolRim,0.72));
        rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(38,rimColor.R,rimColor.G,rimColor.B),1));
        GlassRim.BorderBrush=rimBrush;
        var innerBrush=new LinearGradientBrush{StartPoint=new System.Windows.Point(0,0),EndPoint=new System.Windows.Point(1,1)};
        innerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(22,255,255,255),0));
        innerBrush.GradientStops.Add(new GradientStop(Colors.Transparent,0.42));
        innerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(12,153,208,255),1));
        GlassInnerRim.BorderBrush=innerBrush;
        GlassRim.CornerRadius=new CornerRadius(panelRadius);
        GlassInnerRim.CornerRadius=new CornerRadius(Math.Max(0,panelRadius-1));
        UpdateGlassLayout();
    }
    private void UpdateGlassLayout()
    {
        if(GlassOverlay is null||ActualWidth<=0||ActualHeight<=0)return;
        GlassRim.Width=GlassOverlay.ActualWidth;GlassRim.Height=GlassOverlay.ActualHeight;
        GlassInnerRim.Width=Math.Max(0,GlassOverlay.ActualWidth-3);GlassInnerRim.Height=Math.Max(0,GlassOverlay.ActualHeight-3);
        Canvas.SetLeft(GlassRim,0);Canvas.SetTop(GlassRim,0);Canvas.SetLeft(GlassInnerRim,1.5);Canvas.SetTop(GlassInnerRim,1.5);
        GlassTopSheen.Width=Math.Max(0,GlassOverlay.ActualWidth-40);Canvas.SetLeft(GlassTopSheen,20);Canvas.SetTop(GlassTopSheen,1);
        if(!glassPointerMoved)
        {
            Canvas.SetLeft(GlassGlowA,Math.Max(-90,GlassOverlay.ActualWidth*0.08));Canvas.SetTop(GlassGlowA,-76);
            Canvas.SetLeft(GlassGlowB,Math.Max(0,GlassOverlay.ActualWidth-160));Canvas.SetTop(GlassGlowB,Math.Max(-65,GlassOverlay.ActualHeight-90));
        }
    }
    private void Panel_MouseMove(object sender,System.Windows.Input.MouseEventArgs e)
    {
        if(!acrylicEnabled||glassIntensity==0||GlassOverlay.ActualWidth<=0)return;
        glassPointerMoved=true;
        var p=e.GetPosition(GlassOverlay);
        MoveGlassGlow(GlassGlowA,Math.Clamp(p.X-125,-100,GlassOverlay.ActualWidth-100),Math.Clamp(p.Y-70,-90,GlassOverlay.ActualHeight-60));
        MoveGlassGlow(GlassGlowB,Math.Clamp(GlassOverlay.ActualWidth-p.X-112,-80,GlassOverlay.ActualWidth-120),Math.Clamp(GlassOverlay.ActualHeight-p.Y-62,-75,GlassOverlay.ActualHeight-55));
    }
    private static void MoveGlassGlow(FrameworkElement glow,double left,double top)
    {
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        glow.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(Canvas.GetLeft(glow),left,TimeSpan.FromMilliseconds(260)){EasingFunction=ease});
        glow.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(Canvas.GetTop(glow),top,TimeSpan.FromMilliseconds(320)){EasingFunction=ease});
    }
    private void ApplyAcrylic()
    {
        try
        {
            var hwnd=new WindowInteropHelper(this).Handle;
            if(hwnd==IntPtr.Zero)return;
            // Use DWM's live system backdrop on Windows 11 22H2+. Older Windows
            // versions fall back to the live acrylic accent policy.
            var backdropType=acrylicEnabled?3:1; // TRANSIENTWINDOW (Desktop Acrylic) / NONE
            var hasNativeBackdrop=OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)
                &&DwmSetWindowAttribute(hwnd,38,ref backdropType,sizeof(int))==0;
            var useLegacyAcrylic=acrylicEnabled&&!hasNativeBackdrop;
            if(!hasNativeBackdrop||!acrylicEnabled)
            {
                var policy=new AccentPolicy{AccentState=useLegacyAcrylic?4:0,AccentFlags=2,GradientColor=unchecked((int)0xCC11151C)};
                var size=Marshal.SizeOf<AccentPolicy>(); var memory=Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy,memory,false);var data=new WindowCompositionAttributeData{Attribute=19,Data=memory,Size=size};
                    SetWindowCompositionAttribute(hwnd,ref data);
                }
                finally{Marshal.FreeHGlobal(memory);}
            }
        }
        catch { /* Retain the translucent tint if desktop acrylic is unavailable. */ }
    }
    private void ApplyWindowRegion()
    {
        try
        {
            var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
            var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
            var width=Math.Max(1,(int)Math.Ceiling(ActualWidth*dpi));
            var height=Math.Max(1,(int)Math.Ceiling(ActualHeight*dpi));
            var radius=Math.Max(0,(int)Math.Round(panelRadius*dpi));
            var clipRect=new Rect(0,0,ActualWidth,ActualHeight);
            GlassRoot.Clip=new RectangleGeometry(clipRect,panelRadius,panelRadius);
            GlassTint.Clip=new RectangleGeometry(clipRect,panelRadius,panelRadius);
            GlassOverlay.Clip=new RectangleGeometry(clipRect,panelRadius,panelRadius);
            Panel.Clip=new RectangleGeometry(clipRect,panelRadius,panelRadius);
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))
            {
                // DWM draws the system acrylic backdrop behind the whole HWND.
                // Let DWM round that surface too; a custom HRGN clips WPF content
                // but can leave the compositor's backdrop looking like a square plate.
                var preference=panelRadius==0?1:2; // DONOTROUND / ROUND
                DwmSetWindowAttribute(hwnd,33,ref preference,sizeof(int));
                var noBorder=unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(hwnd,34,ref noBorder,sizeof(int));
                return;
            }
            var region=radius==0?CreateRectRgn(0,0,width,height):CreateRoundRectRgn(0,0,width+1,height+1,radius*2,radius*2);
            if(region==IntPtr.Zero)return;
            if(SetWindowRgn(hwnd,region,true)==0)DeleteObject(region);
        }
        catch{}
    }
    private static bool SystemPrefersLight()
    {
        try { return (int)(Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("AppsUseLightTheme",1)??1)==1; }
        catch{return true;}
    }
    private void RefreshClips()
    {
        var q=QueryBox.Text.Trim(); var matching=allClips.Where(c=>q.Length==0||c.Text.Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();
        clips.Clear(); foreach(var c in matching)clips.Add(c);if(clips.Count>0)ClipboardItems.SelectedIndex=0;
        EmptyText.Text=clips.Count==0?"No clipboard items found":""; EmptyText.Visibility=clips.Count==0?Visibility.Visible:Visibility.Collapsed;
        ContentArea.Visibility=Visibility.Visible;HeaderRule.Visibility=Visibility.Visible;FooterPanel.Visibility=showFooter?Visibility.Visible:Visibility.Collapsed;FooterRule.Visibility=showFooter?Visibility.Visible:Visibility.Collapsed;
        FooterStatus.Text=$"{clips.Count} entries";FooterHints.Text="↵ paste     esc back";UpdateClipPreview();SetPanelHeight();
    }
    private void RefreshSnippets()
    {
        var q=QueryBox.Text.Trim(); var matching=allSnippets.Where(s=>q.Length==0||s.Keyword.Contains(q,StringComparison.OrdinalIgnoreCase)||s.Title.Contains(q,StringComparison.OrdinalIgnoreCase)||s.Body.Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();
        snippets.Clear(); foreach(var s in matching)snippets.Add(s);if(snippets.Count>0)SnippetItems.SelectedIndex=0;
        EmptyText.Text=snippets.Count==0?"No snippets found":""; EmptyText.Visibility=snippets.Count==0?Visibility.Visible:Visibility.Collapsed;
        ContentArea.Visibility=Visibility.Visible;HeaderRule.Visibility=Visibility.Visible;FooterPanel.Visibility=showFooter?Visibility.Visible:Visibility.Collapsed;FooterRule.Visibility=showFooter?Visibility.Visible:Visibility.Collapsed;
        FooterStatus.Text=$"{snippets.Count} snippets";FooterHints.Text="↵ paste     esc back";UpdateSnippetPreview();SetPanelHeight();
    }
    private void ClipboardItems_SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateClipPreview();
    private void SnippetItems_SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateSnippetPreview();
    private void UpdateClipPreview(){if(ClipPreviewText is null)return;if(ClipboardItems.SelectedItem is not ClipItem c){ClipPreviewText.Text="";ClipInfoText.Text="";return;}ClipPreviewText.Text=c.Text;ClipInfoText.Text=$"Content type   Text\nCharacters   {c.Text.Length}\nWords   {c.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length}\nCopied   {c.When}";}
    private void UpdateSnippetPreview(){if(SnippetPreviewText is null)return;if(SnippetItems.SelectedItem is not SnippetItem s){SnippetPreviewText.Text="";SnippetInfoText.Text="";return;}SnippetPreviewText.Text=Expand(s.Body);SnippetInfoText.Text=$"Name   {s.Title}\nKeyword   {s.Keyword}\nCharacters   {s.Body.Length}";}
    private void LaunchSelected()
    {
        if (Results.SelectedItem is not LaunchItem item) return;
        try { Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true}); HideAnimated(); }
        catch (Exception ex) { EmptyText.Text = "Could not open item: " + ex.Message; EmptyText.Visibility = Visibility.Visible; }
    }
    private void CaptureClipboard()
    {
        try { if (!Clipboard.ContainsText()) return; var text=Clipboard.GetText(); if(string.IsNullOrWhiteSpace(text)||text==lastClipboard)return; lastClipboard=text; allClips.Insert(0,new ClipItem(text,DateTime.Now)); while(allClips.Count>100)allClips.RemoveAt(allClips.Count-1); SaveClips(); if(activeMode==1)RefreshClips(); }
        catch { /* Clipboard may be temporarily locked by another process. */ }
    }
    private void LoadClips(){try{if(File.Exists(clipFile))allClips.AddRange(JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(clipFile))??[]);RefreshClips();}catch{} }
    private void SaveClips(){try{File.WriteAllText(clipFile,JsonSerializer.Serialize(allClips));}catch{} }
    private void LoadSnippets()
    {
        try { if(File.Exists(snippetFile)){allSnippets.AddRange(JsonSerializer.Deserialize<List<SnippetItem>>(File.ReadAllText(snippetFile))??[]);} else { allSnippets.Add(new("/date","Current date","{date}")); allSnippets.Add(new("/time","Current time","{time}")); allSnippets.Add(new("/uuid","New UUID","{uuid}")); SaveSnippets(); } RefreshSnippets(); } catch{}
    }
    private void SaveSnippets(){try{File.WriteAllText(snippetFile,JsonSerializer.Serialize(allSnippets));}catch{} }
    private static string Expand(string text) => text.Replace("{date}",DateTime.Now.ToString("yyyy-MM-dd")).Replace("{time}",DateTime.Now.ToString("HH:mm")).Replace("{uuid}",Guid.NewGuid().ToString()).Replace("{clipboard}",Clipboard.ContainsText()?Clipboard.GetText():"");
    private async void PasteText(string text)
    {
        try { Clipboard.SetText(Expand(text)); HideAnimated(); await Task.Delay(180); if(previousForegroundWindow!=IntPtr.Zero){SetForegroundWindow(previousForegroundWindow);await Task.Delay(70);keybd_event(0x11,0,0,UIntPtr.Zero);keybd_event(0x56,0,0,UIntPtr.Zero);keybd_event(0x56,0,2,UIntPtr.Zero);keybd_event(0x11,0,2,UIntPtr.Zero);} }
        catch(Exception ex){EmptyText.Text="Could not paste snippet: "+ex.Message;EmptyText.Visibility=Visibility.Visible;}
    }
    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();
    private void Results_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if(e.Key==Key.Enter){LaunchSelected();e.Handled=true;} }
    private void ClipboardItems_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if(ClipboardItems.SelectedItem is ClipItem c)PasteText(c.Text); }
    private void SnippetItems_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if(SnippetItems.SelectedItem is SnippetItem s)PasteText(s.Body); }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key==Key.Escape){if(activeMode!=0){Back_Click(this,new RoutedEventArgs());}else if(QueryBox.Text.Length>0){QueryBox.Clear();}else HideAnimated();e.Handled=true;return;}
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&e.Key==Key.OemComma){OpenSettings();e.Handled=true;return;}
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&e.Key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2 or Key.D3 or Key.NumPad3)
        {
            activeMode=e.Key is Key.D1 or Key.NumPad1?0:e.Key is Key.D2 or Key.NumPad2?1:2;
            UpdateModeVisibility(); QueryBox.Focus(); QueryBox.SelectAll(); RefreshCurrentMode(); e.Handled=true;return;
        }
        if(e.Key==Key.Enter&&activeMode==0){LaunchSelected();e.Handled=true;return;}
        if(e.Key==Key.Enter&&activeMode==1&&ClipboardItems.SelectedItem is ClipItem clip){PasteText(clip.Text);e.Handled=true;return;}
        if(e.Key==Key.Enter&&activeMode==2&&SnippetItems.SelectedItem is SnippetItem snippet){PasteText(snippet.Body);e.Handled=true;return;}
        if(e.Key is Key.Up or Key.Down)
        {
            var list=activeMode==0?Results:activeMode==1?ClipboardItems:SnippetItems;
            if(activeMode==0&&QueryBox.Text.Length==0)return;
            if(list.Items.Count==0)return;
            var delta=e.Key==Key.Down?1:-1;
            var next=list.SelectedIndex<0?(delta>0?0:list.Items.Count-1):Math.Clamp(list.SelectedIndex+delta,0,list.Items.Count-1);
            list.SelectedIndex=next;
            list.ScrollIntoView(list.SelectedItem);
            e.Handled=true;
        }
    }
    private void Window_Deactivated(object? sender, EventArgs e) { if(IsVisible&&!settingsOpen)HideAnimated(); }
    private void Clear_Click(object sender,RoutedEventArgs e){QueryBox.Clear();QueryBox.Focus();}
    private void Back_Click(object sender,RoutedEventArgs e){activeMode=0;UpdateModeVisibility();QueryBox.Clear();QueryBox.Focus();RefreshResults();}
    private LauncherSettings CurrentSettings()=>new(panelRadius,panelOpacity,acrylicAmount,panelWidth,cornerRadius,shadowAmount,acrylicEnabled,showFooter,glassIntensity,shortcutModifiers,shortcutKey);
    private bool ApplySettings(LauncherSettings s,string mode)
    {
        var shortcutAccepted=string.Equals(s.ShortcutModifiers,shortcutModifiers,StringComparison.OrdinalIgnoreCase)&&string.Equals(s.ShortcutKey,shortcutKey,StringComparison.OrdinalIgnoreCase)
            ||TryRegisterLauncherShortcut(s.ShortcutModifiers,s.ShortcutKey);
        if(shortcutAccepted){shortcutModifiers=s.ShortcutModifiers;shortcutKey=s.ShortcutKey;}
        panelRadius=Math.Clamp(s.PanelRadius,0,36);panelOpacity=Math.Clamp(s.PanelOpacity,20,100);acrylicAmount=Math.Clamp(s.AcrylicAmount,0,100);panelWidth=Math.Clamp(s.PanelWidth,360,900);Width=panelWidth;cornerRadius=Math.Clamp(s.RowRadius,0,24);shadowAmount=Math.Clamp(s.Shadow,0,100);glassIntensity=Math.Clamp(s.GlassIntensity,0,100);acrylicEnabled=s.Acrylic;showFooter=s.ShowFooter;themeMode=mode;ApplyTheme();var footerVisible=showFooter&&(activeMode!=0||QueryBox.Text.Trim().Length>0);FooterPanel.Visibility=footerVisible?Visibility.Visible:Visibility.Collapsed;FooterRule.Visibility=footerVisible?Visibility.Visible:Visibility.Collapsed;SetPanelHeight();return shortcutAccepted;
    }
    private bool TryRegisterLauncherShortcut(string modifiersText,string keyText)
    {
        if(!Enum.TryParse<System.Windows.Input.ModifierKeys>(modifiersText,true,out var modifiers)||modifiers==System.Windows.Input.ModifierKeys.None||
           !Enum.TryParse<System.Windows.Input.Key>(keyText,true,out var key)||key is System.Windows.Input.Key.None or System.Windows.Input.Key.System)return false;
        var virtualKey=System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if(virtualKey<=0)return false;
        var hwnd=new WindowInteropHelper(this).Handle;
        if(hwnd==IntPtr.Zero)return false;
        UnregisterHotKey(hwnd,HotkeyId);
        if(RegisterHotKey(hwnd,HotkeyId,ToNativeModifiers(modifiers),(uint)virtualKey))return true;
        if(Enum.TryParse(shortcutModifiers,true,out System.Windows.Input.ModifierKeys oldModifiers)&&
           Enum.TryParse(shortcutKey,true,out System.Windows.Input.Key oldKey))
            RegisterHotKey(hwnd,HotkeyId,ToNativeModifiers(oldModifiers),(uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(oldKey));
        return false;
    }
    private static uint ToNativeModifiers(System.Windows.Input.ModifierKeys modifiers)
    {
        var result=ModNoRepeat;
        if((modifiers&System.Windows.Input.ModifierKeys.Alt)!=0)result|=ModAlt;
        if((modifiers&System.Windows.Input.ModifierKeys.Control)!=0)result|=ModControl;
        if((modifiers&System.Windows.Input.ModifierKeys.Shift)!=0)result|=ModShift;
        if((modifiers&System.Windows.Input.ModifierKeys.Windows)!=0)result|=ModWindows;
        return result;
    }
    private void OpenSettings()
    {
        var original=CurrentSettings();var originalTheme=themeMode;var wasVisible=IsVisible;
        if(!wasVisible){activeMode=0;QueryBox.Clear();UpdateModeVisibility();RefreshResults();Topmost=true;Show();}
        settingsOpen=true;
        try
        {
            var w=new SettingsWindow(original,originalTheme,(s,m)=>ApplySettings(s,m)){Owner=this,WindowStartupLocation=WindowStartupLocation.Manual};
            var area=SystemParameters.WorkArea;var left=Left+Width+16;if(left+w.Width>area.Right)left=Left-w.Width-16;
            w.Left=Math.Clamp(left,area.Left,Math.Max(area.Left,area.Right-w.Width));
            w.Top=Math.Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-w.Height));
            w.CaptureBackdrop();
            if(w.ShowDialog()==true){ApplySettings(w.Settings,w.ThemeMode);SaveSettings();SaveTheme();}else ApplySettings(original,originalTheme);
        }
        finally{settingsOpen=false;if(!wasVisible)Hide();}
    }
    protected override void OnClosed(EventArgs e){clipboardTimer.Stop();searchDebounceTimer.Stop();iconLoadCancellation?.Cancel();smoothScrollTimer?.Stop();source?.RemoveHook(WndProc);UnregisterHotKey(new WindowInteropHelper(this).Handle,HotkeyId);if(trayIcon is not null){trayIcon.Visible=false;trayIcon.ContextMenuStrip?.Dispose();trayIcon.Dispose();}base.OnClosed(e);}

    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path,uint attributes,ref SHFILEINFO info,uint size,uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct SHFILEINFO{public IntPtr hIcon;public int iIcon;public uint dwAttributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string szDisplayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string szTypeName;}
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey,byte scanCode,uint flags,UIntPtr extraInfo);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint fsModifiers,uint vk);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool UnregisterHotKey(IntPtr hWnd,int id);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll",PreserveSig=true)] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int valueSize);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int widthEllipse,int heightEllipse);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy{public int AccentState;public int AccentFlags;public int GradientColor;public int AnimationId;}
    [StructLayout(LayoutKind.Sequential)] private struct WindowCompositionAttributeData{public int Attribute;public IntPtr Data;public int Size;}
    private sealed record ApplicationEntry(string Name,string Path);
    public sealed class LaunchItem : INotifyPropertyChanged
    {
        private ImageSource? icon;
        public string Name{get;}
        public string Path{get;}
        public string Detail{get;}
        public ImageSource? Icon{get=>icon;set{if(ReferenceEquals(icon,value))return;icon=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Icon)));}}
        public string Glyph=>Detail=="Web search"?"⌕":"◉";
        public LaunchItem(string name,string path,string detail,ImageSource? icon){Name=name;Path=path;Detail=detail;this.icon=icon;}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public record LauncherSettings(int PanelRadius,int PanelOpacity,int AcrylicAmount,int PanelWidth,int RowRadius,int Shadow,bool Acrylic,bool ShowFooter,int GlassIntensity=76,string ShortcutModifiers="Alt",string ShortcutKey="Space");
    public record ClipItem(string Text,DateTime Timestamp){public string Preview=>Text.Replace("\r"," ").Replace("\n"," ");public string When=>Timestamp.ToString("g");}
    public record SnippetItem(string Keyword,string Title,string Body);
}

