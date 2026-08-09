using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/khafre_transfer_title")]
public sealed class KhafreTransferActivity : Activity
{
    private const int MicrophonePermissionRequestCode = 109;

    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly OnDeviceTourGuideService _chatService = new(TourGuideStop.KhafreTransfer);
    private readonly List<TourChatMessage> _history = new();

    private HoldToTalkVoiceAssistant? _voiceAssistant;
    private TextView? _conversation;
    private EditText? _chatInput;
    private TextView? _sendButton;
    private LinearLayout? _chatPanel;
    private TextView? _openChatButton;
    private bool _isMuted;
    private bool _isNavigating;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_khafre_transfer);

        var guide = GetString(Resource.String.khafre_transfer_message);
        var inPageMap = FindViewById<GizaPlateauMapView>(Resource.Id.khafre_transfer_map)!;
        inPageMap.ShowBusPills = !HasPriorityPassTicket();
        FindViewById<TextView>(Resource.Id.khafre_transfer_expand_map_button)!.Click += (_, _) =>
            OpenFullScreenMap();
        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.khafre_transfer_replay_button)!.Click += (_, _) =>
            Narrate(guide);
        FindViewById<TextView>(Resource.Id.khafre_transfer_next_button)!.Click += (_, _) =>
            ContinueToStation();

        _conversation = FindViewById<TextView>(Resource.Id.khafre_transfer_chat_conversation);
        _conversation!.SetText(Resource.String.khafre_transfer_chat_welcome);
        _chatInput = FindViewById<EditText>(Resource.Id.khafre_transfer_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.khafre_transfer_chat_send_button);
        _chatPanel = FindViewById<LinearLayout>(Resource.Id.khafre_transfer_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_khafre_transfer_chat_button);

        var voiceStatus = FindViewById<TextView>(Resource.Id.khafre_transfer_voice_status)!;
        var listenButton = FindViewById<ImageButton>(Resource.Id.khafre_transfer_listen_button)!;
        _voiceAssistant = new HoldToTalkVoiceAssistant(
            this,
            MicrophonePermissionRequestCode,
            listenButton,
            voiceStatus,
            TourGuideStop.KhafreTransfer,
            _history,
            () => guide,
            () => BookingVoiceSummary.BuildForSpeech(this),
            ContinueToStation,
            () => _isMuted,
            AppendConversation);

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
                Narrate(guide);
            }
        };

        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_khafre_transfer_chat_button)!.Click += (_, _) =>
            SetChatExpanded(false);
        _sendButton!.Click += (_, _) => SendChatMessage();

        Narrate(guide);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _isNavigating = false;
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

    private void ContinueToStation()
    {
        if (_isNavigating)
        {
            return;
        }

        _isNavigating = true;
        _voiceAssistant?.CancelListening();
        _voiceService.Stop();
        StartActivity(new Intent(this, typeof(KhafreStationActivity)));
    }

    private void OpenFullScreenMap()
    {
        _voiceAssistant?.CancelListening();
        _voiceService.Stop();
        StartActivity(new Intent(this, typeof(TripProgressActivity)));
    }

    private void SendChatMessage()
    {
        var message = _chatInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _chatInput!.Text = string.Empty;
        AppendConversation(GetString(Resource.String.chat_you), message);
        _history.Add(new TourChatMessage(true, message));
        RequestAiReplyAsync(message);
    }

    private async void RequestAiReplyAsync(string message)
    {
        if (_sendButton is not null)
        {
            _sendButton.Enabled = false;
        }

        try
        {
            var reply = await _chatService.AskAsync(this, _history, message);
            _history.Add(new TourChatMessage(false, reply));
            AppendConversation(GetString(Resource.String.ai_tour_guide_title), reply);
        }
        catch (Exception)
        {
            AppendConversation(GetString(Resource.String.ai_tour_guide_title), GetString(Resource.String.ai_chat_error_local));
        }
        finally
        {
            if (_sendButton is not null)
            {
                _sendButton.Enabled = true;
            }
        }
    }

    private void SetChatExpanded(bool expanded)
    {
        if (_chatPanel is not null)
        {
            _chatPanel.Visibility = expanded ? ViewStates.Visible : ViewStates.Gone;
        }

        if (_openChatButton is not null)
        {
            _openChatButton.Visibility = expanded ? ViewStates.Gone : ViewStates.Visible;
        }

        if (expanded)
        {
            _ = _chatService.PrepareForChatAsync(this);
            _chatInput?.RequestFocus();
        }
    }

    private void AppendConversation(string speaker, string message)
    {
        if (_conversation is null)
        {
            return;
        }

        var previous = _conversation.Text?.ToString();
        _conversation.Text = string.IsNullOrWhiteSpace(previous)
            ? $"{speaker}\n{message}"
            : $"{previous}\n\n{speaker}\n{message}";
    }

    private static bool HasPriorityPassTicket() =>
        CartStore.CurrentItems.Any(item =>
            item.Quantity > 0 &&
            !item.Type.StartsWith("Access Pass", StringComparison.OrdinalIgnoreCase));
}
