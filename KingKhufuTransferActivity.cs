using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/king_khufu_transfer_title")]
public sealed class KingKhufuTransferActivity : Activity
{
    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly OnDeviceTourGuideService _chatService = new(TourGuideStop.KhufuTransfer);
    private readonly List<TourChatMessage> _history = new();
    private bool _isMuted;
    private TextView? _conversation;
    private EditText? _chatInput;
    private TextView? _sendButton;
    private LinearLayout? _chatPanel;
    private TextView? _openChatButton;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_sphinx_station);

        var isPriorityPass = HasPriorityPassTicket();
        var guide = GetString(isPriorityPass
            ? Resource.String.king_khufu_transfer_priority_message
            : Resource.String.king_khufu_transfer_message);
        FindViewById<TextView>(Resource.Id.sphinx_station_eyebrow_text)!
            .SetText(isPriorityPass
                ? Resource.String.king_khufu_transfer_priority_eyebrow
                : Resource.String.king_khufu_transfer_eyebrow);
        FindViewById<TextView>(Resource.Id.sphinx_station_title_text)!
            .SetText(Resource.String.king_khufu_transfer_title);
        FindViewById<TextView>(Resource.Id.sphinx_station_message_text)!.Text = guide;

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.sphinx_station_replay_button)!.Click += (_, _) =>
        {
            Narrate(guide);
        };
        FindViewById<TextView>(Resource.Id.sphinx_station_next_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            StartActivity(new Android.Content.Intent(this, typeof(KingKhufuJourneyActivity)));
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

        _chatPanel = FindViewById<LinearLayout>(Resource.Id.sphinx_station_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_sphinx_station_chat_button);
        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_sphinx_station_chat_button)!.Click += (_, _) =>
            SetChatExpanded(false);
        _conversation = FindViewById<TextView>(Resource.Id.sphinx_station_chat_conversation);
        _conversation!.SetText(isPriorityPass
            ? Resource.String.king_khufu_transfer_priority_chat_welcome
            : Resource.String.king_khufu_transfer_chat_welcome);
        _chatInput = FindViewById<EditText>(Resource.Id.sphinx_station_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.sphinx_station_chat_send_button);
        _sendButton!.Click += (_, _) => SendChatMessage();

        Narrate(guide);
    }

    protected override void OnPause()
    {
        _voiceService.Stop();
        base.OnPause();
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
            var reply = await _chatService.AskAsync(this, _history, message);
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
