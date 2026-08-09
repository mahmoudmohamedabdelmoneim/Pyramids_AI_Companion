using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/ticket_access_title")]
public sealed class TicketAccessGuideActivity : Activity
{
    private const int MicrophonePermissionRequestCode = 104;

    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly List<TourChatMessage> _voiceHistory = new();
    private HoldToTalkVoiceAssistant? _voiceAssistant;
    private string _narration = string.Empty;
    private bool _isMuted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_ticket_access_guide);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.ticket_access_next_button)!.Click += (_, _) =>
            ContinueToCheckpoint();

        var bookingSummary = BookingVoiceSummary.BuildDisplay(this);
        FindViewById<TextView>(Resource.Id.ticket_access_summary)!.Text = bookingSummary;
        _narration = string.Join("\n\n",
            GetString(Resource.String.ticket_access_intro_singular),
            GetString(Resource.String.ticket_access_ready),
            GetString(Resource.String.ticket_access_voice_prompt));

        FindViewById<TextView>(Resource.Id.replay_guide_button)!.Click += (_, _) =>
            Narrate(_narration);

        var voiceStatus = FindViewById<TextView>(Resource.Id.ticket_access_voice_status)!;
        var listenButton = FindViewById<ImageButton>(Resource.Id.ticket_access_listen_button)!;
        _voiceAssistant = new HoldToTalkVoiceAssistant(
            this,
            MicrophonePermissionRequestCode,
            listenButton,
            voiceStatus,
            TourGuideStop.TicketAccess,
            _voiceHistory,
            () => _narration,
            () => BookingVoiceSummary.BuildForSpeech(this),
            ContinueToCheckpoint,
            () => _isMuted);

        FindViewById<Switch>(Resource.Id.mute_guide_switch)!.CheckedChange += (_, args) =>
        {
            _isMuted = args.IsChecked;
            AiSpeechPreferences.SetMuted(this, _isMuted);
            if (_isMuted)
            {
                _voiceService.Stop();
            }
            else
            {
                Narrate(_narration);
            }
        };

        Narrate(_narration);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _voiceAssistant?.SetActive(true);
    }

    protected override void OnPause()
    {
        _voiceAssistant?.SetActive(false);
        _voiceService.Stop();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _voiceAssistant?.Destroy();
        _voiceAssistant = null;
        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        _voiceAssistant?.HandlePermissionResult(requestCode, grantResults);
    }

    private void ContinueToCheckpoint()
    {
        _voiceService.Stop();
        StartActivity(new Intent(this, typeof(CheckpointActivity)));
    }

    private async void Narrate(string text)
    {
        _voiceAssistant?.CancelListening();
        if (_isMuted)
        {
            return;
        }

        if (await _voiceService.TrySpeakAsync(this, text))
        {
            return;
        }

        var detail = _voiceService.LastError;
        var message = string.IsNullOrWhiteSpace(detail)
            ? GetString(Resource.String.offline_voice_unavailable)
            : $"{GetString(Resource.String.offline_voice_unavailable)} ({detail})";
        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }
}
