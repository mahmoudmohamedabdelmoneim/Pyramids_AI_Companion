using Android.Animation;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;

namespace AndroidApp1;

/// <summary>
/// Applies the modern interaction and type system consistently to XML and runtime-created views.
/// Keeping this in one lifecycle-aware layer prevents individual screens from drifting apart.
/// </summary>
internal static class ModernUi
{
    private const float PressedScale = 0.982f;
    private const long PressInDuration = 85L;
    private const long PressOutDuration = 170L;
    private const int PressedHighlightAlpha = 72;
    private const float ClickHighlightCornerRadiusDp = 4f;
    private const long ShadeInDuration = 65L;
    private const long ShadeOutDuration = 150L;

    public static void Attach(Activity activity)
    {
        ConfigureWindow(activity);

        if (activity.Window?.DecorView is not ViewGroup decor)
        {
            return;
        }

        DecorateTree(decor);
        InstallDynamicViewObserver(decor);
    }

    public static void Refresh(Activity activity)
    {
        if (activity.Window?.DecorView is ViewGroup decor)
        {
            DecorateTree(decor);
        }
    }

    private static void ConfigureWindow(Activity activity)
    {
        var window = activity.Window;
        if (window is null)
        {
            return;
        }

        // Android 15 and earlier still use explicit system-bar colors. Android 16 owns
        // edge-to-edge bar treatment, while the theme supplies the correct dark icon mode.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            window.SetStatusBarColor(Color.ParseColor("#061713"));
            window.SetNavigationBarColor(Color.ParseColor("#04100D"));
        }

        ApplyNavigationBarInset(activity);
    }

    private static void ApplyNavigationBarInset(Activity activity)
    {
        // Reserve the obscured bottom area once at the activity content root so every screen's
        // controls remain tappable with gesture navigation, three-button navigation, the
        // on-screen keyboard, and devices that have a bottom display cutout.
        // On older Android versions the framework normally consumes this inset itself; in that
        // case the listener receives zero and leaves the existing content padding unchanged.
        var content = activity.FindViewById<View>(Android.Resource.Id.Content);
        if (content is null || content.GetTag(Resource.Id.modern_ui_navigation_insets) is not null)
        {
            return;
        }

        var listener = new NavigationBarInsetsListener(
            content.PaddingLeft,
            content.PaddingTop,
            content.PaddingRight,
            content.PaddingBottom);
        content.SetTag(Resource.Id.modern_ui_navigation_insets, listener);
        content.SetOnApplyWindowInsetsListener(listener);
        content.RequestApplyInsets();
    }

    private static void InstallDynamicViewObserver(ViewGroup root)
    {
        if (root.GetTag(Resource.Id.modern_ui_observer) is not null)
        {
            return;
        }

        var observer = new DynamicLayoutObserver(root);
        root.SetTag(Resource.Id.modern_ui_observer, observer);
        root.ViewTreeObserver?.AddOnGlobalLayoutListener(observer);
    }

    private static void DecorateTree(View view)
    {
        if (view.GetTag(Resource.Id.modern_ui_styled) is null)
        {
            DecorateView(view);
            view.SetTag(Resource.Id.modern_ui_styled, Java.Lang.Boolean.True);
        }

        if (view is not ViewGroup group)
        {
            return;
        }

        for (var index = 0; index < group.ChildCount; index++)
        {
            var child = group.GetChildAt(index);
            if (child is not null)
            {
                DecorateTree(child);
            }
        }
    }

    private static void DecorateView(View view)
    {
        if (view.Id == Resource.Id.back_button)
        {
            view.Visibility = ViewStates.Gone;
            view.Enabled = false;
            view.Clickable = false;
            view.Focusable = false;
            view.ImportantForAccessibility = ImportantForAccessibility.No;
            return;
        }

        if (view is TextView textView)
        {
            ImproveTypography(textView);
        }

        if (IsActionControl(view))
        {
            ImproveActionControl(view);
        }

        AttachClickHighlight(view);
    }

    private static bool IsActionControl(View view) =>
        view.Clickable &&
        view is not EditText &&
        view is not Switch &&
        view is TextView or ImageButton or ImageView;

    private static void ImproveTypography(TextView textView)
    {
        textView.SetLineSpacing(
            Math.Max(Dp(textView.Context, 1), textView.LineSpacingExtra),
            Math.Max(1.04f, textView.LineSpacingMultiplier));

        if (textView.Clickable &&
            textView is not EditText &&
            OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var metrics = textView.Resources!.DisplayMetrics!;
            var fontScale = Math.Max(0.5f, textView.Resources.Configuration?.FontScale ?? 1f);
            var currentSp = Math.Max(11, (int)Math.Round(textView.TextSize / (metrics.Density * fontScale)));
            var minimumSp = Math.Max(10, currentSp - 4);
            textView.SetAutoSizeTextTypeUniformWithConfiguration(
                minimumSp,
                currentSp,
                1,
                (int)ComplexUnitType.Sp);
        }
    }

    private static void ImproveActionControl(View view)
    {
        view.Focusable = true;
        view.SoundEffectsEnabled = true;
        view.SetMinimumHeight(Math.Max(view.MinimumHeight, Dp(view.Context, 48)));
        view.ImportantForAccessibility = ImportantForAccessibility.Yes;

        if (view is TextView textView && string.IsNullOrWhiteSpace(view.ContentDescription))
        {
            view.ContentDescription = textView.Text;
        }

        var usesClickHighlight = IsClickHighlightControl(view);
        if (!usesClickHighlight && view.Background is not RippleDrawable)
        {
            var rippleColors = new ColorStateList(
                new[]
                {
                    new[] { Android.Resource.Attribute.StatePressed },
                    new[] { Android.Resource.Attribute.StateFocused },
                    Array.Empty<int>()
                },
                new[]
                {
                    unchecked((int)0x4EFFFFFF),
                    unchecked((int)0x2EFFFFFF),
                    unchecked((int)0x1AFFFFFF)
                });

            var mask = new GradientDrawable();
            mask.SetShape(ShapeType.Rectangle);
            mask.SetColor(Color.White);
            mask.SetCornerRadius(Dp(view.Context, 18));

            view.Background = new RippleDrawable(rippleColors, view.Background?.Mutate(), mask);
        }

        if (!usesClickHighlight)
        {
            view.StateListAnimator = CreatePressAnimator(view);
        }
    }

    private static void AttachClickHighlight(View view)
    {
        if (!IsClickHighlightControl(view) ||
            view.GetTag(Resource.Id.modern_ui_click_highlight_bound) is not null)
        {
            return;
        }

        view.SetTag(Resource.Id.modern_ui_click_highlight_bound, Java.Lang.Boolean.True);

        var highlight = new GradientDrawable();
        highlight.SetShape(ShapeType.Rectangle);
        highlight.SetColor(Color.ParseColor("#FFF2CE"));
        highlight.SetCornerRadius(Dp(view.Context, ClickHighlightCornerRadiusDp));
        highlight.Alpha = 0;
        view.Overlay?.Add(highlight);
        view.Post(() => highlight.SetBounds(0, 0, view.Width, view.Height));

        view.Touch += (_, args) =>
        {
            switch (args.Event?.ActionMasked)
            {
                case MotionEventActions.Down:
                    highlight.SetBounds(0, 0, view.Width, view.Height);
                    AnimateClickHighlight(
                        view,
                        highlight,
                        PressedHighlightAlpha,
                        ShadeInDuration);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    AnimateClickHighlight(view, highlight, 0, ShadeOutDuration);
                    break;
            }
        };
    }

    private static bool IsClickHighlightControl(View view)
    {
        if (view is ImageButton)
        {
            return string.Equals(
                view.ContentDescription,
                view.Context?.GetString(Resource.String.voice_assistant_mic_description),
                StringComparison.Ordinal);
        }

        return view is TextView textView &&
            string.Equals(
                textView.Text?.ToString()?.Trim(),
                view.Context?.GetString(Resource.String.replay_ai_speech),
                StringComparison.OrdinalIgnoreCase);
    }

    private static void AnimateClickHighlight(
        View view,
        Drawable highlight,
        int targetAlpha,
        long duration)
    {
        if (!view.Enabled && targetAlpha > 0)
        {
            return;
        }

        (view.GetTag(Resource.Id.modern_ui_click_highlight_animator) as Animator)?.Cancel();
        var animator = ObjectAnimator.OfInt(highlight, "alpha", targetAlpha)!;
        animator.SetDuration(duration);
        animator.SetInterpolator(new DecelerateInterpolator(2f));
        view.SetTag(Resource.Id.modern_ui_click_highlight_animator, animator);
        animator.Start();
    }

    private static StateListAnimator CreatePressAnimator(View view)
    {
        var pressed = new AnimatorSet();
        pressed.PlayTogether(
            ObjectAnimator.OfFloat(view, "scaleX", PressedScale)!,
            ObjectAnimator.OfFloat(view, "scaleY", PressedScale)!,
            ObjectAnimator.OfFloat(view, "translationZ", -Dp(view.Context, 1))!);
        pressed.SetDuration(PressInDuration);
        pressed.SetInterpolator(new DecelerateInterpolator(2f));

        var released = new AnimatorSet();
        released.PlayTogether(
            ObjectAnimator.OfFloat(view, "scaleX", 1f)!,
            ObjectAnimator.OfFloat(view, "scaleY", 1f)!,
            ObjectAnimator.OfFloat(view, "translationZ", 0f)!);
        released.SetDuration(PressOutDuration);
        released.SetInterpolator(new DecelerateInterpolator(1.8f));

        var animator = new StateListAnimator();
        animator.AddState(new[] { Android.Resource.Attribute.StatePressed }, pressed);
        animator.AddState(Array.Empty<int>(), released);
        return animator;
    }

    private static int Dp(Context? context, float value)
    {
        var density = context?.Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)Math.Round(value * density);
    }

    private sealed class DynamicLayoutObserver(ViewGroup root) : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly WeakReference<ViewGroup> _root = new(root);

        public void OnGlobalLayout()
        {
            if (_root.TryGetTarget(out var root))
            {
                DecorateTree(root);
            }
        }
    }

    private sealed class NavigationBarInsetsListener(
        int initialLeft,
        int initialTop,
        int initialRight,
        int initialBottom) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var navigationBottom = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? insets.GetInsets(
                    WindowInsets.Type.NavigationBars() |
                    WindowInsets.Type.Ime() |
                    WindowInsets.Type.DisplayCutout()).Bottom
                : insets.SystemWindowInsetBottom;
            view.SetPadding(
                initialLeft,
                initialTop,
                initialRight,
                initialBottom + navigationBottom);
            return insets;
        }
    }

}
