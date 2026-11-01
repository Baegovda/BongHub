using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BongHub.Next;

/// <summary>Short, GPU-friendly animations (opacity + render transforms only; never layout).</summary>
public static class Fx
{
    public static bool Enabled { get; set; } = true;
    public static readonly IEasingFunction Out = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction OutQuint = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });
    /// <summary>Gentle spring-like overshoot.</summary>
    public static readonly IEasingFunction Spring = Freeze(new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut });
    static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }
    static Duration D(int ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 1));

    public static DoubleAnimation Anim(double to, int ms, IEasingFunction? ease = null) => new(to, D(ms)) { EasingFunction = ease ?? Out };
    public static DoubleAnimation Anim(double from, double to, int ms, IEasingFunction? ease = null) => new(from, to, D(ms)) { EasingFunction = ease ?? Out };

    static ScaleTransform EnsureScale(UIElement el)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        if (el.RenderTransform is ScaleTransform s && !s.IsFrozen) return s;
        var st = new ScaleTransform(1, 1); el.RenderTransform = st; return st;
    }
    static TranslateTransform EnsureTranslate(UIElement el)
    {
        if (el.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
        var tt = new TranslateTransform(); el.RenderTransform = tt; return tt;
    }

    /// <summary>Window/content open: fade in + scale 0.965 → 1 with a soft spring (≈240ms).</summary>
    public static void Appear(UIElement el, double fromScale = 0.965, int ms = 240)
    {
        var st = EnsureScale(el);
        el.BeginAnimation(UIElement.OpacityProperty, Anim(0, 1, (int)(ms * 0.7)));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(fromScale, 1, ms, Spring));
        st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(fromScale, 1, ms, Spring));
    }

    /// <summary>Close: fade out + scale to 0.97 (≈150ms), then <paramref name="done"/>.</summary>
    public static void Disappear(UIElement el, Action done, int ms = 150)
    {
        if (!Enabled) { done(); return; }
        var st = EnsureScale(el);
        var a = Anim(0, ms); a.Completed += (_, _) => done();
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.97, ms));
        st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.97, ms));
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>Page change: content slides up 14px and fades in (≈260ms).</summary>
    public static void SlideIn(UIElement el, double dy = 14, int ms = 260)
    {
        var t = EnsureTranslate(el);
        el.BeginAnimation(UIElement.OpacityProperty, Anim(0, 1, (int)(ms * 0.8)));
        t.BeginAnimation(TranslateTransform.YProperty, Anim(dy, 0, ms, OutQuint));
    }

    public static void Fade(UIElement el, double to, int ms = 150) => el.BeginAnimation(UIElement.OpacityProperty, Anim(to, ms));
    public static void MoveX(TranslateTransform t, double to, int ms = 280) => t.BeginAnimation(TranslateTransform.XProperty, Anim(to, ms, Spring));
    public static void MoveY(TranslateTransform t, double to, int ms = 280) => t.BeginAnimation(TranslateTransform.YProperty, Anim(to, ms, Spring));
}

/// <summary>
/// Window plumbing shared by every Next window: dark DWM frame, rounded corners, optional Mica,
/// and the anti-white-flash sequence (DWM cloak from HWND creation until WPF has committed its first frame).
/// </summary>
public static class WindowFx
{
    public sealed class State { public IntPtr Hwnd; public bool Backdrop; public int CloakedAtCreate = -1; public int CloakedAfterReveal = -1; public double FirstFrameMs = -1; public bool Revealed; public System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew(); public Action? AfterFirstFrame; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, State> States = new();
    public static State Of(Window w) => States.GetOrCreateValue(w);

    public static void Attach(Window w, UIElement content, bool wantBackdrop, Color solid)
    {
        var st = Of(w);
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle; st.Hwnd = h;
            if (w is MainWindow) AppEnv.Mark("main_hwnd");
            Dwm.Cloak(h, true);                    // nothing reaches the screen until our first frame exists
            st.CloakedAtCreate = Dwm.Cloaked(h);
            Dwm.Dark(h); Dwm.Round(h); Dwm.BorderColor(h, Color.FromRgb(0x2A, 0x2E, 0x3A));
            var src = HwndSource.FromHwnd(h);
            st.Backdrop = wantBackdrop && Dwm.Backdrop(h, 2);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = st.Backdrop ? Colors.Transparent : solid;
            w.Background = st.Backdrop ? Brushes.Transparent : new SolidColorBrush(solid);
        };
        w.ContentRendered += (_, _) =>
        {
            st.FirstFrameMs = st.Clock.Elapsed.TotalMilliseconds;
            if (w is MainWindow) AppEnv.Mark("main_content_rendered");
            if (Fx.Enabled) { content.Opacity = 0; Fx.Appear(content); }
            // reveal after one more render pass so the compositor surely has a painted frame
            w.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                Dwm.Cloak(st.Hwnd, false); st.Revealed = true; st.CloakedAfterReveal = Dwm.Cloaked(st.Hwnd);
                st.AfterFirstFrame?.Invoke();
            });
        };
    }
}
