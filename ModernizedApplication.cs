using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Util;

namespace AndroidApp1;

[Application]
public sealed class ModernizedApplication : Application, Application.IActivityLifecycleCallbacks
{
    public ModernizedApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        RegisterActivityLifecycleCallbacks(this);
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
    {
        activity.Window?.DecorView?.Post(() => TryApplyModernUi(activity, attach: true));
    }

    public void OnActivityResumed(Activity activity)
    {
        AiSpeechPreferences.Bind(activity);
        TryApplyModernUi(activity, attach: false);
    }

    private static void TryApplyModernUi(Activity activity, bool attach)
    {
        try
        {
            if (activity.IsFinishing || activity.IsDestroyed)
            {
                return;
            }

            if (attach)
            {
                ModernUi.Attach(activity);
            }
            else
            {
                ModernUi.Refresh(activity);
            }
        }
        catch (Exception exception)
        {
            // The visual enhancement layer is optional. A device-specific framework or
            // drawable failure must not bring down the activity it is decorating.
            Log.Error("ModernUi", $"Unable to decorate {activity.GetType().Name}: {exception}");
        }
    }

    public void OnActivityStarted(Activity activity)
    {
    }

    public void OnActivityPaused(Activity activity)
    {
    }

    public void OnActivityStopped(Activity activity)
    {
    }

    public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
    {
    }

    public void OnActivityDestroyed(Activity activity)
    {
    }
}
