using Android.OS;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

[Activity(Label = "@string/access_pass_trip_end_title")]
public sealed class AccessPassTripEndActivity : Activity
{
    public const string PriorityPassModeExtra = "priority_pass_trip_end_mode";

    private readonly List<TourChatMessage> _history = new();
    private OnDeviceTourGuideService? _chatService;
    private bool _isPriorityPass;
    private LinearLayout? _chatPanel;
    private ScrollView? _feedbackPanel;
    private TextView? _openChatButton;
    private TextView? _conversation;
    private EditText? _chatInput;
    private TextView? _sendButton;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_access_pass_trip_end);

        _isPriorityPass = Intent?.GetBooleanExtra(PriorityPassModeExtra, false) == true;
        _chatService = new OnDeviceTourGuideService(_isPriorityPass
            ? TourGuideStop.PriorityPassTripEnd
            : TourGuideStop.AccessPassTripEnd);
        ApplyPageContent();

        var map = FindViewById<GizaPlateauMapView>(Resource.Id.access_pass_trip_end_map)!;
        map.ShowBusPills = true;
        map.ContentDescription = GetString(_isPriorityPass
            ? Resource.String.priority_pass_trip_end_map_description
            : Resource.String.access_pass_trip_end_map_description);

        _feedbackPanel = FindViewById<ScrollView>(Resource.Id.access_pass_trip_feedback_panel);
        FindViewById<TextView>(Resource.Id.access_pass_end_trip_button)!.Click += (_, _) =>
            SetFeedbackVisible(true);
        FindViewById<TextView>(Resource.Id.submit_trip_feedback_button)!.Click += (_, _) =>
            SubmitFeedback();

        _chatPanel = FindViewById<LinearLayout>(Resource.Id.access_pass_trip_end_chat_panel);
        _openChatButton = FindViewById<TextView>(Resource.Id.open_access_pass_trip_end_chat_button);
        _openChatButton!.Click += (_, _) => SetChatExpanded(true);
        FindViewById<TextView>(Resource.Id.close_access_pass_trip_end_chat_button)!.Click += (_, _) =>
            SetChatExpanded(false);
        _conversation = FindViewById<TextView>(Resource.Id.access_pass_trip_end_chat_conversation);
        _conversation!.SetText(_isPriorityPass
            ? Resource.String.priority_pass_trip_end_chat_welcome
            : Resource.String.access_pass_trip_end_chat_welcome);
        _chatInput = FindViewById<EditText>(Resource.Id.access_pass_trip_end_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.access_pass_trip_end_chat_send_button);
        _sendButton!.Click += (_, _) => SendChatMessage();
    }

    private void ApplyPageContent()
    {
        if (!_isPriorityPass)
        {
            return;
        }

        Title = GetString(Resource.String.priority_pass_trip_end_title);
        FindViewById<TextView>(Resource.Id.access_pass_trip_end_eyebrow)!
            .SetText(Resource.String.priority_pass_trip_end_eyebrow);
        FindViewById<TextView>(Resource.Id.access_pass_trip_end_title_text)!
            .SetText(Resource.String.priority_pass_trip_end_title);
        var messageText =
            FindViewById<TextView>(Resource.Id.access_pass_trip_end_message_text)!;
        messageText.SetText(Resource.String.priority_pass_trip_end_message);
        messageText.Gravity = GravityFlags.Start;
        messageText.TextAlignment = TextAlignment.ViewStart;
        var leaseExtensionButton =
            FindViewById<TextView>(Resource.Id.request_lease_extension_button)!;
        leaseExtensionButton.Visibility = ViewStates.Visible;
        leaseExtensionButton.Click += (_, _) =>
            StartActivity(new Android.Content.Intent(this, typeof(GolfCartLeaseExtensionActivity)));
        FindViewById<TextView>(Resource.Id.access_pass_trip_end_map_hint_text)!
            .SetText(Resource.String.priority_pass_trip_end_map_hint);
    }

    public override void OnBackPressed()
    {
        if (_feedbackPanel?.Visibility == ViewStates.Visible)
        {
            SetFeedbackVisible(false);
            return;
        }

        if (_chatPanel?.Visibility == ViewStates.Visible)
        {
            SetChatExpanded(false);
            return;
        }

        Finish();
    }

    private void SubmitFeedback()
    {
        var rating = FindViewById<RatingBar>(Resource.Id.trip_overall_rating)!;
        if (rating.Rating <= 0)
        {
            Toast.MakeText(this, Resource.String.trip_feedback_rating_required, ToastLength.Short)?.Show();
            return;
        }

        var submitButton = FindViewById<TextView>(Resource.Id.submit_trip_feedback_button)!;
        submitButton.Enabled = false;
        submitButton.SetText(Resource.String.trip_feedback_submitted_button);
        FindViewById<TextView>(Resource.Id.trip_feedback_confirmation)!.Visibility = ViewStates.Visible;
        Toast.MakeText(this, Resource.String.trip_feedback_submitted, ToastLength.Long)?.Show();
    }

    private void SetFeedbackVisible(bool visible)
    {
        if (_feedbackPanel is null)
        {
            return;
        }

        if (visible)
        {
            SetChatExpanded(false);
        }

        _feedbackPanel.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        if (visible)
        {
            _feedbackPanel.ScrollTo(0, 0);
        }
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
            if (_feedbackPanel is not null)
            {
                _feedbackPanel.Visibility = ViewStates.Gone;
            }

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
}
