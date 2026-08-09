using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/panorama_station_title")]
public sealed class PanoramaStationActivity : Activity
{
    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly OnDeviceTourGuideService _chatService = new(TourGuideStop.PanoramaStation);
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
        SetContentView(Resource.Layout.activity_panorama_station);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.panorama_station_replay_button)!.Click += (_, _) => Narrate();
        FindViewById<TextView>(Resource.Id.panorama_station_next_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            StartActivity(new Android.Content.Intent(this, typeof(PanoramaArrivalActivity)));
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
                Narrate();
            }
        };

        _chatPanel = FindViewById<LinearLayout>(Resource.Id.panorama_station_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_panorama_station_chat_button);
        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_panorama_station_chat_button)!.Click += (_, _) => SetChatExpanded(false);
        _conversation = FindViewById<TextView>(Resource.Id.panorama_station_chat_conversation);
        _chatInput = FindViewById<EditText>(Resource.Id.panorama_station_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.panorama_station_chat_send_button);
        _sendButton!.Click += (_, _) => SendChatMessage();

        Narrate();
    }

    protected override void OnPause()
    {
        _voiceService.Stop();
        base.OnPause();
    }

    private async void Narrate()
    {
        if (_isMuted)
        {
            return;
        }

        var message = GetString(Resource.String.panorama_station_message);
        if (await _voiceService.TrySpeakAsync(this, message))
        {
            return;
        }

        var detail = _voiceService.LastError;
        var error = string.IsNullOrWhiteSpace(detail)
            ? GetString(Resource.String.offline_voice_unavailable)
            : $"{GetString(Resource.String.offline_voice_unavailable)} ({detail})";
        Toast.MakeText(this, error, ToastLength.Long)?.Show();
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
}
