using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/checkpoint_title")]
public sealed class CheckpointActivity : Activity
{
    private const int MicrophonePermissionRequestCode = 105;

    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly OnDeviceTourGuideService _chatService = new(TourGuideStop.Checkpoint);
    private readonly List<TourChatMessage> _history = new();

    private HoldToTalkVoiceAssistant? _voiceAssistant;
    private TextView? _conversation;
    private EditText? _chatInput;
    private TextView? _sendButton;
    private LinearLayout? _chatPanel;
    private TextView? _openChatButton;
    private bool _isMuted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_checkpoint);

        var instructions = GetString(Resource.String.checkpoint_instructions);
        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) =>
        {
            _voiceService.Stop();
            Finish();
        };
        FindViewById<TextView>(Resource.Id.checkpoint_replay_button)!.Click += (_, _) =>
            Narrate(instructions);
        FindViewById<TextView>(Resource.Id.open_ticket_pdf_button)!.Click += (_, _) =>
            OpenLatestTicketPdf();
        FindViewById<TextView>(Resource.Id.checkpoint_all_done_button)!.Click += (_, _) =>
            CompleteCheckpoint();

        _conversation = FindViewById<TextView>(Resource.Id.checkpoint_chat_conversation);
        _chatInput = FindViewById<EditText>(Resource.Id.checkpoint_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.checkpoint_chat_send_button);
        _chatPanel = FindViewById<LinearLayout>(Resource.Id.checkpoint_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_checkpoint_chat_button);

        var voiceStatus = FindViewById<TextView>(Resource.Id.checkpoint_voice_status)!;
        var listenButton = FindViewById<ImageButton>(Resource.Id.checkpoint_listen_button)!;
        _voiceAssistant = new HoldToTalkVoiceAssistant(
            this,
            MicrophonePermissionRequestCode,
            listenButton,
            voiceStatus,
            TourGuideStop.Checkpoint,
            _history,
            () => instructions,
            () => BookingVoiceSummary.BuildForSpeech(this),
            CompleteCheckpoint,
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
                Narrate(instructions);
            }
        };

        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_checkpoint_chat_button)!.Click += (_, _) =>
            SetChatExpanded(false);
        _sendButton!.Click += (_, _) => SendChatMessage();

        Narrate(instructions);
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

    private void OpenLatestTicketPdf()
    {
        var ticketPdfPath = TicketPdfGenerator.GetLatestSavedTicketPdfPath(this);
        if (string.IsNullOrWhiteSpace(ticketPdfPath))
        {
            Toast.MakeText(this, Resource.String.ticket_pdf_missing, ToastLength.Long)?.Show();
            return;
        }

        var intent = new Intent(this, typeof(TicketPdfViewerActivity));
        intent.PutExtra(TicketPdfViewerActivity.TicketPdfPathExtra, ticketPdfPath);
        StartActivity(intent);
    }

    private void CompleteCheckpoint()
    {
        _voiceService.Stop();
        StartActivity(new Intent(this, typeof(ExhibitionHallActivity)));
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
