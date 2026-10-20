using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace AintoWindows;

public partial class SettingsWindow : Window
{
    public MainWindow.LauncherSettings Settings { get; private set; }
    public string ThemeMode { get; private set; }
    private readonly Func<MainWindow.LauncherSettings,string,bool> preview;
    private string shortcutModifiers;
    private string shortcutKey;
    private string acceptedShortcutModifiers;
    private string acceptedShortcutKey;
    private bool capturingShortcut;
    private bool shortcutRejected;

    public SettingsWindow(MainWindow.LauncherSettings settings, string themeMode, Func<MainWindow.LauncherSettings,string,bool> preview)
    {
        InitializeComponent();
        try{SettingsLogo.Source=new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory,"logo.png"),UriKind.Absolute));}catch{}
        this.preview = preview;
        Settings = settings;
        ThemeMode = themeMode;
        shortcutModifiers=string.IsNullOrWhiteSpace(settings.ShortcutModifiers)?"Alt":settings.ShortcutModifiers;
        shortcutKey=string.IsNullOrWhiteSpace(settings.ShortcutKey)?"Space":settings.ShortcutKey;
        acceptedShortcutModifiers=shortcutModifiers;acceptedShortcutKey=shortcutKey;
        ShortcutCaptureButton.Content=FormatShortcut(shortcutModifiers,shortcutKey);
        PanelRadiusSlider.Value = settings.PanelRadius;
        OpacitySlider.Value = settings.PanelOpacity;
        AcrylicSlider.Value = settings.AcrylicAmount;
        GlassIntensitySlider.Value = settings.GlassIntensity;
        WidthSlider.Value = settings.PanelWidth;
        RowRadiusSlider.Value = settings.RowRadius;
        ShadowSlider.Value = settings.Shadow;
        AcrylicToggle.IsChecked = settings.Acrylic;
        FooterToggle.IsChecked = settings.ShowFooter;
        UpdateValues();
        AcrylicSlider.ValueChanged += (_, _) => SettingsChanged();
        GlassIntensitySlider.ValueChanged += (_, _) => SettingsChanged();
        OpacitySlider.ValueChanged += (_, _) => SettingsChanged();
        PanelRadiusSlider.ValueChanged += (_, _) => SettingsChanged();
        RowRadiusSlider.ValueChanged += (_, _) => SettingsChanged();
        ShadowSlider.ValueChanged += (_, _) => SettingsChanged();
        WidthSlider.ValueChanged += (_, _) => SettingsChanged();
        AcrylicToggle.Checked += (_, _) => { AcrylicSlider.IsEnabled = true; OpacitySlider.IsEnabled = true; SettingsChanged(); };
        AcrylicToggle.Unchecked += (_, _) => { AcrylicSlider.IsEnabled = false; OpacitySlider.IsEnabled = false; SettingsChanged(); };
        FooterToggle.Checked += (_, _) => SettingsChanged();
        FooterToggle.Unchecked += (_, _) => SettingsChanged();
        AcrylicSlider.IsEnabled = settings.Acrylic;
        OpacitySlider.IsEnabled = settings.Acrylic;
    }

    public void CaptureBackdrop()
    {
        try
        {
            var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
            var x=(int)Math.Round(Left*dpi);var y=(int)Math.Round(Top*dpi);
            var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x,y)).Bounds;
            x=Math.Clamp(x,screen.Left,screen.Right-1);y=Math.Clamp(y,screen.Top,screen.Bottom-1);
            var width=Math.Min(Math.Max(1,(int)Math.Ceiling(Width*dpi)),screen.Right-x);
            var height=Math.Min(Math.Max(1,(int)Math.Ceiling(Height*dpi)),screen.Bottom-y);
            using var bitmap=new System.Drawing.Bitmap(width,height);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x,y,0,0,new System.Drawing.Size(width,height),System.Drawing.CopyPixelOperation.SourceCopy);
            var handle=bitmap.GetHbitmap();
            try
            {
                var image=Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                if(image.CanFreeze)image.Freeze();
                SettingsBackdrop.Source=image;
            }
            finally{DeleteObject(handle);}
            SettingsBackdrop.Width=width/dpi;SettingsBackdrop.Height=height/dpi;
            SettingsBackdrop.Effect=new BlurEffect{Radius=18,KernelType=KernelType.Gaussian};
        }
        catch{SettingsBackdrop.Source=null;}
    }

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SettingsChanged();
        if(shortcutRejected)return;
        DialogResult = true;
    }

    private void SettingsChanged()
    {
        if (AcrylicSlider is null) return;
        Settings = new MainWindow.LauncherSettings((int)PanelRadiusSlider.Value, (int)OpacitySlider.Value,
            (int)AcrylicSlider.Value, (int)WidthSlider.Value, (int)RowRadiusSlider.Value,
            (int)ShadowSlider.Value, AcrylicToggle.IsChecked == true, FooterToggle.IsChecked == true,
            (int)GlassIntensitySlider.Value,shortcutModifiers,shortcutKey);
        UpdateValues();
        var applied=preview(Settings,ThemeMode);
        shortcutRejected=!applied;
        if(applied){acceptedShortcutModifiers=shortcutModifiers;acceptedShortcutKey=shortcutKey;}
        ShortcutStatus.Text=applied?"Click the shortcut, then press a key combination.":"That shortcut is already in use. The previous shortcut is still active.";
    }

    private void ShortcutCaptureButton_Click(object sender,RoutedEventArgs e)
    {
        capturingShortcut=true;
        ShortcutCaptureButton.Content="Press shortcut…";
        ShortcutStatus.Text="Press at least one modifier (Ctrl, Alt, Shift, or Win) and a key. Esc cancels.";
        Focus();
    }

    private void SettingsWindow_PreviewKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(!capturingShortcut)return;
        var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
        if(key is System.Windows.Input.Key.Escape){capturingShortcut=false;shortcutModifiers=acceptedShortcutModifiers;shortcutKey=acceptedShortcutKey;ShortcutCaptureButton.Content=FormatShortcut(shortcutModifiers,shortcutKey);SettingsChanged();ShortcutStatus.Text="Shortcut unchanged.";e.Handled=true;return;}
        if(key is System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
        {e.Handled=true;return;}
        var modifiers=System.Windows.Input.Keyboard.Modifiers;
        if(modifiers==System.Windows.Input.ModifierKeys.None)
        {ShortcutStatus.Text="Include Ctrl, Alt, Shift, or Win with the key.";e.Handled=true;return;}
        shortcutModifiers=modifiers.ToString();
        shortcutKey=key.ToString();
        capturingShortcut=false;
        ShortcutCaptureButton.Content=FormatShortcut(shortcutModifiers,shortcutKey);
        SettingsChanged();
        e.Handled=true;
    }

    private static string FormatShortcut(string modifiers,string key)
    {
        var modifierText=modifiers.Replace("Control","Ctrl").Replace(", "," + ");
        var keyText=key.StartsWith("D",StringComparison.Ordinal)&&key.Length==2&&char.IsDigit(key[1])?key[1].ToString():key;
        return $"{modifierText} + {keyText}";
    }

    private void UpdateValues()
    {
        AcrylicValue.Text = $"{(int)AcrylicSlider.Value}%";
        OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
        GlassIntensityValue.Text = $"{(int)GlassIntensitySlider.Value}%";
        PanelRadiusValue.Text = $"{(int)PanelRadiusSlider.Value}px";
        RowRadiusValue.Text = $"{(int)RowRadiusSlider.Value}px";
        ShadowValue.Text = $"{(int)ShadowSlider.Value}%";
        WidthValue.Text = $"{(int)WidthSlider.Value}px";
    }
}
