using Android.App;
using Android.Content;
using Android.Widget;
using System.Runtime.CompilerServices;

namespace AndroidApp1;

internal static class AiSpeechPreferences
{
    private const string PreferencesName = "ai_speech_preferences";
    private const string MutedKey = "is_muted";
    private static readonly ConditionalWeakTable<Switch, object> BoundSwitches = new();

    public static bool IsMuted(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)?
            .GetBoolean(MutedKey, false) == true;

    public static void Bind(Activity activity)
    {
        var muteSwitch = activity.FindViewById<Switch>(Resource.Id.mute_guide_switch);
        if (muteSwitch is null)
        {
            return;
        }

        if (BoundSwitches.TryGetValue(muteSwitch, out _))
        {
            muteSwitch.Checked = IsMuted(activity);
            return;
        }

        muteSwitch.Checked = IsMuted(activity);
        muteSwitch.CheckedChange += (_, args) => SetMuted(activity, args.IsChecked);
        BoundSwitches.Add(muteSwitch, new object());
    }

    internal static void SetMuted(Context context, bool isMuted)
    {
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        var editor = preferences?.Edit();
        editor?.PutBoolean(MutedKey, isMuted);
        editor?.Apply();
    }
}
