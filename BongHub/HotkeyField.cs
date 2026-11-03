using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace BongHub;

/// <summary>Click to capture a chord. Accent pulse while listening. Esc cancels, Backspace clears.</summary>
public sealed class HotkeyField : Border
{
    readonly StackPanel caps = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock placeholder = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    string value = "";
    bool listening;
    DropShadowEffect? glow;

    public HotkeyField()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(8, 4, 8, 4);
        MinWidth = 148;
        MinHeight = 28;
        BorderThickness = new Thickness(1);
        Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
        var grid = new Grid();
        grid.Children.Add(caps);
        grid.Children.Add(placeholder);
        Child = grid;
        Tag = "hotkey-field";
        ToolTip = "클릭한 뒤 단축키를 누르세요. Esc는 취소, Backspace는 지워요.";
        MouseLeftButtonDown += (_, e) => { Begin(); e.Handled = true; };
        LostKeyboardFocus += (_, _) => { if (listening) Cancel(); };
        Paint();
    }

    public string Value => value;

    public event Action<string>? Gesture;

    public void SetValue(string chord)
    {
        value = chord ?? "";
        listening = false;
        StopPulse();
        Paint();
    }

    public void Confirm()
    {
        listening = false;
        StopPulse();
        Paint();
        if (!Fx.Enabled) return;
        var scale = new ScaleTransform(1, 1);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        var anim = new DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(120)) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    public void Shake()
    {
        listening = false;
        StopPulse();
        Paint();
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
        if (!Fx.Enabled) return;
        var shift = new TranslateTransform();
        RenderTransform = shift;
        var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(210))));
        shift.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    public void Cancel()
    {
        listening = false;
        StopPulse();
        Paint();
    }

    void Begin()
    {
        listening = true;
        Focus();
        Keyboard.Focus(this);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x8A, 0xFE));
        BorderThickness = new Thickness(1.5);
        glow = new DropShadowEffect { Color = Color.FromRgb(0x4C, 0x8A, 0xFE), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 };
        Effect = glow;
        if (Fx.Enabled)
        {
            var pulse = new DoubleAnimation(0.35, 0.95, TimeSpan.FromMilliseconds(720))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, pulse);
        }
        Paint();
    }

    void StopPulse()
    {
        if (glow != null) glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        glow = null;
        Effect = null;
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
        RenderTransform = Transform.Identity;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!listening)
        {
            if (e.Key is Key.Enter or Key.Space) { Begin(); e.Handled = true; }
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifier(key)) { Paint(); return; }
        if (key == Key.Escape) { Cancel(); Gesture?.Invoke("escape"); return; }
        if (key == Key.Back) { Gesture?.Invoke("back"); return; }
        if (key is Key.Tab or Key.None) return;
        string text = Format(key, Keyboard.Modifiers);
        if (text.Length == 0) return;
        Gesture?.Invoke(text);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (!listening) return;
        Paint();
    }

    void Paint()
    {
        caps.Children.Clear();
        if (listening)
        {
            string live = HotkeyChord.Caps(
                (Keyboard.Modifiers & ModifierKeys.Windows) != 0,
                (Keyboard.Modifiers & ModifierKeys.Control) != 0,
                (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            if (live.Length == 0)
            {
                placeholder.Text = HotkeyChord.ListenPrompt;
                placeholder.Opacity = 1;
                placeholder.Visibility = Visibility.Visible;
                return;
            }
            placeholder.Visibility = Visibility.Collapsed;
            FillCaps(live);
            return;
        }
        if (value.Length == 0)
        {
            placeholder.Text = HotkeyChord.ListenPrompt;
            placeholder.Opacity = 0.55;
            placeholder.Visibility = Visibility.Visible;
            return;
        }
        placeholder.Visibility = Visibility.Collapsed;
        FillCaps(value.Replace("+", " + "));
    }

    void FillCaps(string text)
    {
        var parts = text.Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) caps.Children.Add(new TextBlock { Text = "+", Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontSize = 12 });
            if (parts[i] == "…")
            {
                caps.Children.Add(new TextBlock { Text = "…", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 14 });
                continue;
            }
            caps.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x28, 255, 255, 255)),
                Padding = new Thickness(7, 2, 7, 2),
                Child = new TextBlock { Text = parts[i], FontSize = 12.5 }
            });
        }
    }

    public static string Format(Key key, ModifierKeys mods)
    {
        if (IsModifier(key) || key == Key.None) return "";
        var parts = new List<string>();
        if ((mods & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");
        if ((mods & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        parts.Add(Token(key));
        return string.Join("+", parts);
    }

    static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    static string Token(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + (int)(key - Key.NumPad0);
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        return key switch
        {
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Escape => "Escape",
            Key.Enter => "Enter",
            Key.OemPlus => "Oemplus",
            Key.OemMinus => "OemMinus",
            _ => key.ToString(),
        };
    }
}
