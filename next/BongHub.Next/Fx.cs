using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BongHub.Next;

/// <summary>Short animations (opacity + render transforms only; never layout). Clocks are removed when they finish so a window does not keep a full-size intermediate surface.</summary>
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

    /// <summary>
    /// One clock per property. FillBehavior.Stop plus an explicit base value when it ends.
    /// A held opacity or scale clock keeps a full-window intermediate surface for the life of the window.
    /// A newer clock on the same property invalidates the older completion so it cannot clear the new one.
    /// </summary>
    sealed class ClockGen { public readonly Dictionary<DependencyProperty, int> Map = new(); }
    static readonly ConditionalWeakTable<DependencyObject, ClockGen> Clocks = new();
    static int Bump(DependencyObject target, DependencyProperty dp)
    {
        var map = Clocks.GetOrCreateValue(target).Map;
        map.TryGetValue(dp, out int n);
        map[dp] = n + 1;
        return n + 1;
    }
    static bool IsCurrent(DependencyObject target, DependencyProperty dp, int token)
        => Clocks.TryGetValue(target, out var gen) && gen.Map.TryGetValue(dp, out int cur) && cur == token;
    static void Play(DependencyObject target, DependencyProperty dp, DoubleAnimation anim, Action done)
    {
        anim.FillBehavior = FillBehavior.Stop;
        int token = Bump(target, dp);
        anim.Completed += (_, _) =>
        {
            if (!IsCurrent(target, dp, token)) return;
            target.BeginAnimation(dp, null);
            done();
        };
        target.BeginAnimation(dp, anim);
    }

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

    /// <summary>
    /// Window/content open: fade in + scale 0.965 → 1 with a soft spring (≈240ms).
    /// The clock is removed when it finishes. A held opacity animation keeps a full-window intermediate surface.
    /// </summary>
    public static void Appear(UIElement el, double fromScale = 0.965, int ms = 240)
    {
        var st = EnsureScale(el);
        // Drop any in-flight completion before removing clocks. BeginAnimation(null) must not reapply the old base.
        Bump(el, UIElement.OpacityProperty);
        Bump(st, ScaleTransform.ScaleXProperty);
        Bump(st, ScaleTransform.ScaleYProperty);
        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.Opacity = 1;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        st.ScaleX = 1;
        st.ScaleY = 1;
        Play(el, UIElement.OpacityProperty, Anim(0, 1, (int)(ms * 0.7)), () => el.Opacity = 1);
        Play(st, ScaleTransform.ScaleXProperty, Anim(fromScale, 1, ms, Spring), () =>
        {
            st.ScaleX = 1;
            if (ReferenceEquals(el.RenderTransform, st) && Math.Abs(st.ScaleY - 1) < 0.001)
                el.RenderTransform = Transform.Identity;
        });
        Play(st, ScaleTransform.ScaleYProperty, Anim(fromScale, 1, ms, Spring), () =>
        {
            st.ScaleY = 1;
            if (ReferenceEquals(el.RenderTransform, st) && Math.Abs(st.ScaleX - 1) < 0.001)
                el.RenderTransform = Transform.Identity;
        });
    }

    /// <summary>Close: fade out + scale to 0.97 (≈150ms), then <paramref name="done"/>.</summary>
    public static void Disappear(UIElement el, Action done, int ms = 150)
    {
        if (!Enabled) { done(); return; }
        var st = EnsureScale(el);
        // Invalidate an in-flight Appear/Fade so its completion cannot restore opacity 1 and cancel this close.
        Bump(el, UIElement.OpacityProperty);
        Bump(st, ScaleTransform.ScaleXProperty);
        Bump(st, ScaleTransform.ScaleYProperty);
        var a = Anim(0, ms); a.Completed += (_, _) => done();
        st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.97, ms));
        st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.97, ms));
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>Page change: content slides up 14px and fades in (≈260ms).</summary>
    public static void SlideIn(UIElement el, double dy = 14, int ms = 260)
    {
        var t = EnsureTranslate(el);
        Bump(el, UIElement.OpacityProperty);
        Bump(t, TranslateTransform.YProperty);
        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.Opacity = 1;
        t.BeginAnimation(TranslateTransform.YProperty, null);
        t.Y = 0;
        Play(el, UIElement.OpacityProperty, Anim(0, 1, (int)(ms * 0.8)), () => el.Opacity = 1);
        Play(t, TranslateTransform.YProperty, Anim(dy, 0, ms, OutQuint), () =>
        {
            t.Y = 0;
            if (ReferenceEquals(el.RenderTransform, t)) el.RenderTransform = Transform.Identity;
        });
    }

    public static void Fade(UIElement el, double to, int ms = 150)
        => Play(el, UIElement.OpacityProperty, Anim(to, ms), () => el.Opacity = to);
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
            var target = src?.CompositionTarget;
            if (target != null) target.RenderMode = RenderMode.SoftwareOnly;
            // ProcessRenderMode is the real switch (it overrides a per-target Default). Software presents an opaque client:
            // a #D0 tint over a transparent target is premultiplied against black and looks darker than #0D1018.
            // DWM Mica is still requested; the solid fallback (the colors used when Mica is unavailable) covers it.
            bool software = RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly
                || target == null
                || target.RenderMode == RenderMode.SoftwareOnly;
            bool micaApi = wantBackdrop && Dwm.Backdrop(h, 2);
            bool mica = micaApi && !software;
            st.Backdrop = mica;
            if (target != null) target.BackgroundColor = mica ? Colors.Transparent : solid;
            var solidBrush = new SolidColorBrush(solid);
            solidBrush.Freeze();
            w.Background = mica ? Brushes.Transparent : solidBrush;
        };
        w.ContentRendered += (_, _) =>
        {
            st.FirstFrameMs = st.Clock.Elapsed.TotalMilliseconds;
            if (w is MainWindow) AppEnv.Mark("main_content_rendered");
            if (Fx.Enabled) Fx.Appear(content);
            // reveal after one more render pass so the compositor surely has a painted frame
            w.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                Dwm.Cloak(st.Hwnd, false); st.Revealed = true; st.CloakedAfterReveal = Dwm.Cloaked(st.Hwnd);
                st.AfterFirstFrame?.Invoke();
            });
        };
    }
}
