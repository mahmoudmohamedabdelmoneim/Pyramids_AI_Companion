using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/destination_guide_title")]
public sealed class DestinationGuideActivity : Activity
{
    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly OnDeviceTourGuideService _chatService = new(TourGuideStop.NextDestination);
    private readonly List<TourChatMessage> _history = new();
    private string _message = string.Empty;
    private bool _isMuted;
    private TextView? _conversation;
    private EditText? _chatInput;
    private TextView? _sendButton;
    private LinearLayout? _chatPanel;
    private TextView? _openChatButton;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_destination_guide);

        _message = HasPriorityPassTicket()
            ? GetString(Resource.String.priority_pass_destination)
            : GetString(Resource.String.access_pass_destination);

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.destination_guide_message)!.Text = _message;
        var inPageMap = FindViewById<GizaPlateauMapView>(Resource.Id.destination_guide_map)!;
        inPageMap.ShowBusPills = !HasPriorityPassTicket();
        FindViewById<TextView>(Resource.Id.destination_guide_expand_map_button)!.Click += (_, _) =>
            OpenFullScreenMap();
        FindViewById<TextView>(Resource.Id.destination_guide_replay_button)!.Click += (_, _) => Narrate();
        FindViewById<TextView>(Resource.Id.destination_guide_next_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            StartActivity(new Android.Content.Intent(this, typeof(PanoramaStationActivity)));
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

        _chatPanel = FindViewById<LinearLayout>(Resource.Id.destination_guide_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_destination_guide_chat_button);
        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_destination_guide_chat_button)!.Click += (_, _) => SetChatExpanded(false);
        _conversation = FindViewById<TextView>(Resource.Id.destination_guide_chat_conversation);
        _chatInput = FindViewById<EditText>(Resource.Id.destination_guide_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.destination_guide_chat_send_button);
        _sendButton!.Click += (_, _) => SendChatMessage();

        Narrate();
    }

    private void OpenFullScreenMap()
    {
        _voiceService.Stop();
        StartActivity(new Android.Content.Intent(this, typeof(TripProgressActivity)));
    }

    private async void Narrate()
    {
        if (_isMuted || string.IsNullOrWhiteSpace(_message))
        {
            return;
        }

        if (await _voiceService.TrySpeakAsync(this, _message))
        {
            return;
        }

        var detail = _voiceService.LastError;
        var message = string.IsNullOrWhiteSpace(detail)
            ? GetString(Resource.String.offline_voice_unavailable)
            : $"{GetString(Resource.String.offline_voice_unavailable)} ({detail})";
        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }

    private static bool HasPriorityPassTicket() =>
        CartStore.IssuedTickets.Any(ticket =>
            !ticket.Type.StartsWith("Access Pass", StringComparison.OrdinalIgnoreCase));

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
