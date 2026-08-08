using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/access_pass_refreshments_title")]
public sealed class AccessPassRefreshmentsActivity : Activity
{
    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly List<TourChatMessage> _history = new();
    private OnDeviceTourGuideService? _chatService;
    private bool _isPriorityPass;
    private bool _isMuted;
    private TextView? _conversation;
    private EditText? _chatInput;
    private TextView? _sendButton;
    private LinearLayout? _chatPanel;
    private TextView? _openChatButton;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_access_pass_refreshments);

        _isPriorityPass = HasPriorityPassTicket();
        _chatService = new OnDeviceTourGuideService(_isPriorityPass
            ? TourGuideStop.PriorityPassRefreshments
            : TourGuideStop.AccessPassRefreshments);
        ApplyPageContent();
        var guide = BuildNarration();

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.access_pass_refreshments_replay_button)!.Click += (_, _) =>
        {
            Narrate(guide);
        };
        FindViewById<TextView>(Resource.Id.access_pass_refreshments_next_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            var intent = new Android.Content.Intent(this, typeof(AccessPassTripEndActivity));
            intent.PutExtra(AccessPassTripEndActivity.PriorityPassModeExtra, _isPriorityPass);
            StartActivity(intent);
        };
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

        _chatPanel = FindViewById<LinearLayout>(Resource.Id.access_pass_refreshments_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_access_pass_refreshments_chat_button);
        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_access_pass_refreshments_chat_button)!.Click += (_, _) =>
            SetChatExpanded(false);
        _conversation = FindViewById<TextView>(Resource.Id.access_pass_refreshments_chat_conversation);
        _conversation!.SetText(_isPriorityPass
            ? Resource.String.priority_pass_refreshments_chat_welcome
            : Resource.String.access_pass_refreshments_chat_welcome);
        _chatInput = FindViewById<EditText>(Resource.Id.access_pass_refreshments_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.access_pass_refreshments_chat_send_button);
        _sendButton!.Click += (_, _) => SendChatMessage();

        Narrate(guide);
    }

    protected override void OnPause()
    {
        _voiceService.Stop();
        base.OnPause();
    }

    private void ApplyPageContent()
    {
        if (!_isPriorityPass)
        {
            return;
        }

        FindViewById<TextView>(Resource.Id.refreshments_eyebrow_text)!
            .SetText(Resource.String.priority_pass_refreshments_eyebrow);
        FindViewById<TextView>(Resource.Id.refreshments_intro_text)!
            .SetText(Resource.String.priority_pass_refreshments_intro);
        FindViewById<TextView>(Resource.Id.refreshments_lounge_intro_text)!
            .SetText(Resource.String.priority_pass_refreshments_lounge_intro);
        FindViewById<TextView>(Resource.Id.king_khufu_center_services_text)!
            .SetText(Resource.String.priority_king_khufu_center_services);
        FindViewById<TextView>(Resource.Id.nine_pyramids_lounge_services_text)!
            .SetText(Resource.String.priority_nine_pyramids_lounge_services);
    }

    private string BuildNarration()
    {
        var intro = _isPriorityPass
            ? Resource.String.priority_pass_refreshments_intro
            : Resource.String.access_pass_refreshments_intro;
        var loungeIntro = _isPriorityPass
            ? Resource.String.priority_pass_refreshments_lounge_intro
            : Resource.String.access_pass_refreshments_lounge_intro;
        var kingKhufuServices = _isPriorityPass
            ? Resource.String.priority_king_khufu_center_services
            : Resource.String.king_khufu_center_services;
        var ninePyramidsServices = _isPriorityPass
            ? Resource.String.priority_nine_pyramids_lounge_services
            : Resource.String.nine_pyramids_lounge_services;

        return string.Join(
            "\n\n",
            GetString(intro),
            GetString(loungeIntro),
            GetString(Resource.String.access_pass_refreshments_services_intro),
            GetString(Resource.String.king_khufu_center_heading),
            GetString(kingKhufuServices),
            GetString(Resource.String.nine_pyramids_lounge_heading),
            GetString(ninePyramidsServices));
    }

    private async void Narrate(string text)
    {
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
            var reply = await _chatService!.AskAsync(this, _history, message);
            _history.Add(new TourChatMessage(false, reply));
            AppendConversation(GetString(Resource.String.ai_tour_guide_title), reply);
        }
        catch (Exception)
        {
            AppendConversation(
                GetString(Resource.String.ai_tour_guide_title),
                GetString(Resource.String.ai_chat_error_local));
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
            _ = _chatService!.PrepareForChatAsync(this);
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
