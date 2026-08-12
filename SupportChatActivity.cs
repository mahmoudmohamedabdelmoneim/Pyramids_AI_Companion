using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace AndroidApp1;

[Activity(
    Label = "@string/support_chat_title",
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class SupportChatActivity : Activity
{
    public const string EmergencyModeExtra = "support_chat_emergency_mode";
    public const string InitialMessageExtra = "support_chat_initial_message";
    private const int CaptureReportPhotoRequestCode = 2401;
    private const int PickReportPhotoRequestCode = 2402;

    private readonly OnDeviceSupportService _chatService = new();
    private readonly List<TourChatMessage> _history = new();
    private LinearLayout? _messages;
    private ScrollView? _messagesScroll;
    private EditText? _messageInput;
    private TextView? _sendButton;
    private TextView? _reportPhotoCameraButton;
    private TextView? _reportPhotoGalleryButton;
    private TextView? _skipReportPhotoButton;
    private bool _isReplyPending;
    private bool _requestAcknowledged;
    private bool _isClosing;
    private bool _isEmergency;
    private bool _reportPhotoOfferAdded;
    private bool _reportPhotoCaptured;
    private bool _completionPendingForPhoto;
    private FeedbackCameraAssistance _cameraAssistance;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_support_chat);
        _isEmergency = Intent?.GetBooleanExtra(EmergencyModeExtra, false) == true;

        FindViewById<TextView>(Resource.Id.back_button)!.Click += (_, _) => CloseScreen();

        _messages = FindViewById<LinearLayout>(Resource.Id.support_chat_messages);
        _messagesScroll = FindViewById<ScrollView>(Resource.Id.support_chat_scroll);
        _messageInput = FindViewById<EditText>(Resource.Id.support_chat_input);
        _sendButton = FindViewById<TextView>(Resource.Id.support_chat_send_button);
        ConfigureChatMode();

        _sendButton!.Click += (_, _) => SendMessage();

        AddMessage(
            GetString(Resource.String.support_chat_assistant_name),
            GetString(_isEmergency
                ? Resource.String.emergency_chat_welcome
                : Resource.String.support_chat_welcome),
            isUser: false);
        _ = _chatService.PrepareForChatAsync(this);

        var initialMessage = Intent?.GetStringExtra(InitialMessageExtra)?.Trim();
        if (!string.IsNullOrWhiteSpace(initialMessage))
        {
            _messageInput!.Text = initialMessage;
            _messageInput.Post(SendMessage);
        }
    }

    public override void OnBackPressed()
    {
        CloseScreen();
    }

    protected override void OnDestroy()
    {
        _isClosing = true;
        base.OnDestroy();
    }

    private void SendMessage()
    {
        if (_isReplyPending || _completionPendingForPhoto)
        {
            return;
        }

        var feedback = _messageInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return;
        }

        _messageInput!.Text = string.Empty;
        AddMessage(GetString(Resource.String.chat_you), feedback, isUser: true);
        _history.Add(new TourChatMessage(true, feedback));
        HideKeyboard();
        RequestAiReplyAsync();
    }

    private async void RequestAiReplyAsync()
    {
        _isReplyPending = true;
        SetComposerEnabled(false);
        var pendingMessage = AddMessage(
            GetString(Resource.String.support_chat_assistant_name),
            GetString(Resource.String.support_chat_thinking),
            isUser: false);

        try
        {
            var purpose = _isEmergency
                ? SupportChatPurpose.Emergency
                : SupportChatPurpose.Feedback;
            var reply = await _chatService.RespondAsync(
                this,
                _history,
                purpose,
                _requestAcknowledged);
            if (_isClosing)
            {
                return;
            }

            RemoveMessage(pendingMessage);
            if (reply.HasReasonableContext)
            {
                if (!string.IsNullOrWhiteSpace(reply.Text))
                {
                    _history.Add(new TourChatMessage(false, reply.Text));
                    AddMessage(
                        GetString(Resource.String.support_chat_assistant_name),
                        reply.Text,
                        isUser: false);
                }

                if (OfferReportPhotoCaptureIfNeeded(waitForChoice: true))
                {
                    return;
                }

                AcknowledgeRequest(reply.Text);
                return;
            }

            _history.Add(new TourChatMessage(false, reply.Text));
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                reply.Text,
                isUser: false);
            OfferReportPhotoCaptureIfNeeded(waitForChoice: false);
        }
        catch (Exception)
        {
            if (_isClosing)
            {
                return;
            }

            RemoveMessage(pendingMessage);
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                GetString(Resource.String.support_chat_error_local),
                isUser: false);
            OfferReportPhotoCaptureIfNeeded(waitForChoice: false);
        }
        finally
        {
            _isReplyPending = false;
            if (!_isClosing)
            {
                SetComposerEnabled(!_completionPendingForPhoto);
            }
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok ||
            requestCode is not CaptureReportPhotoRequestCode and not PickReportPhotoRequestCode)
        {
            return;
        }

        var fromGallery = requestCode == PickReportPhotoRequestCode;
        var photo = fromGallery
            ? data?.Data is { } photoUri
                ? LoadGalleryPhoto(photoUri)
                : null
            : data?.Extras?.Get("data") as Bitmap;
        if (photo is null)
        {
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                GetString(fromGallery
                    ? Resource.String.report_photo_gallery_failed
                    : Resource.String.seller_photo_capture_failed),
                isUser: false);
            return;
        }

        _reportPhotoCaptured = true;
        if (_reportPhotoCameraButton is not null)
        {
            _reportPhotoCameraButton.Enabled = false;
            _reportPhotoCameraButton.SetText(Resource.String.report_photo_attached_action);
        }

        if (_reportPhotoGalleryButton is not null)
        {
            _reportPhotoGalleryButton.Enabled = false;
            _reportPhotoGalleryButton.SetText(Resource.String.report_photo_attached_action);
        }

        _history.Add(new TourChatMessage(
            true,
            _cameraAssistance switch
            {
                FeedbackCameraAssistance.Person =>
                    "Person photo attached to the report as supporting evidence.",
                FeedbackCameraAssistance.SellerOrProvider =>
                    "Seller/provider photo attached to the report as supporting evidence.",
                _ => "Visible-problem photo attached to the report as supporting evidence."
            }));
        AddCapturedPhoto(photo);

        if (_completionPendingForPhoto)
        {
            AcknowledgeRequest();
        }
        else if (!_isReplyPending)
        {
            RequestAiReplyAsync();
        }
    }

    private bool OfferReportPhotoCaptureIfNeeded(bool waitForChoice)
    {
        if (_reportPhotoCaptured)
        {
            return false;
        }

        var plan = FeedbackConversationPlanner.Analyze(_history);
        var currentAssistance = FeedbackConversationPlanner.GetCameraAssistance(plan);
        if (currentAssistance == FeedbackCameraAssistance.None)
        {
            return false;
        }

        _cameraAssistance = currentAssistance;
        if (_reportPhotoOfferAdded)
        {
            if (!waitForChoice ||
                _reportPhotoCameraButton is null ||
                _reportPhotoGalleryButton is null)
            {
                return false;
            }

            _completionPendingForPhoto = true;
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                GetString(Resource.String.report_photo_final_choice),
                isUser: false);
            _messages?.RemoveView(_reportPhotoCameraButton);
            _messages?.RemoveView(_reportPhotoGalleryButton);
            AddPhotoActionButton(_reportPhotoCameraButton, widthDp: 180);
            AddPhotoActionButton(_reportPhotoGalleryButton, widthDp: 220);
            AddSkipPhotoButton();
            ScrollMessagesToBottom();
            return true;
        }

        _reportPhotoOfferAdded = true;
        _completionPendingForPhoto = waitForChoice;
        AddMessage(
            GetString(Resource.String.support_chat_assistant_name),
            GetString(_cameraAssistance switch
            {
                FeedbackCameraAssistance.Person => Resource.String.report_photo_person_offer,
                FeedbackCameraAssistance.SellerOrProvider => Resource.String.report_photo_provider_offer,
                _ => Resource.String.report_photo_issue_offer
            }),
            isUser: false);

        _reportPhotoCameraButton = CreatePhotoActionButton(
            Resource.String.open_camera,
            Resource.Drawable.next_button,
            "#09251F");
        _reportPhotoCameraButton.Click += (_, _) => OpenReportCamera();
        AddPhotoActionButton(_reportPhotoCameraButton, widthDp: 180);

        _reportPhotoGalleryButton = CreatePhotoActionButton(
            Resource.String.choose_from_gallery,
            Resource.Drawable.replay_guide_button,
            "#FFF2CE");
        _reportPhotoGalleryButton.Click += (_, _) => OpenReportGallery();
        AddPhotoActionButton(_reportPhotoGalleryButton, widthDp: 220);

        if (waitForChoice)
        {
            AddSkipPhotoButton();
        }

        ScrollMessagesToBottom();
        return true;
    }

    private void OpenReportCamera()
    {
        if (_reportPhotoCaptured)
        {
            return;
        }

        var cameraIntent = new Intent(MediaStore.ActionImageCapture);
        if (cameraIntent.ResolveActivity(PackageManager!) is null)
        {
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                GetString(Resource.String.seller_photo_camera_unavailable),
                isUser: false);
            return;
        }

        StartActivityForResult(cameraIntent, CaptureReportPhotoRequestCode);
    }

    private void OpenReportGallery()
    {
        if (_reportPhotoCaptured)
        {
            return;
        }

        var galleryIntent = new Intent(Intent.ActionOpenDocument);
        galleryIntent.AddCategory(Intent.CategoryOpenable);
        galleryIntent.SetType("image/*");
        galleryIntent.AddFlags(ActivityFlags.GrantReadUriPermission);
        if (galleryIntent.ResolveActivity(PackageManager!) is null)
        {
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                GetString(Resource.String.report_photo_gallery_unavailable),
                isUser: false);
            return;
        }

        StartActivityForResult(galleryIntent, PickReportPhotoRequestCode);
    }

    private Bitmap? LoadGalleryPhoto(Android.Net.Uri photoUri)
    {
        try
        {
            using var boundsStream = ContentResolver?.OpenInputStream(photoUri);
            if (boundsStream is null)
            {
                return null;
            }

            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            _ = BitmapFactory.DecodeStream(boundsStream, null, bounds);
            var sampleSize = 1;
            while (bounds.OutWidth / sampleSize > 1600 || bounds.OutHeight / sampleSize > 1600)
            {
                sampleSize *= 2;
            }

            using var imageStream = ContentResolver?.OpenInputStream(photoUri);
            return imageStream is null
                ? null
                : BitmapFactory.DecodeStream(
                    imageStream,
                    null,
                    new BitmapFactory.Options { InSampleSize = sampleSize });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void AddCapturedPhoto(Bitmap photo)
    {
        AddMessage(
            GetString(Resource.String.chat_you),
            GetString(_cameraAssistance switch
            {
                FeedbackCameraAssistance.Person => Resource.String.report_photo_person_attached,
                FeedbackCameraAssistance.SellerOrProvider => Resource.String.report_photo_provider_attached,
                _ => Resource.String.report_photo_issue_attached
            }),
            isUser: true);

        var image = new ImageView(this);
        image.SetImageBitmap(photo);
        image.SetScaleType(ImageView.ScaleType.CenterCrop);
        image.SetBackgroundResource(Resource.Drawable.support_chat_user_bubble);
        image.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));

        var layout = new LinearLayout.LayoutParams(Dp(240), Dp(180))
        {
            Gravity = GravityFlags.End
        };
        layout.SetMargins(0, 0, 0, Dp(12));
        _messages?.AddView(image, layout);
        ScrollMessagesToBottom();
    }

    private TextView CreatePhotoActionButton(int textResource, int backgroundResource, string textColor)
    {
        var button = new TextView(this)
        {
            Text = GetString(textResource),
            TextSize = 12,
            Gravity = GravityFlags.Center,
            Clickable = true,
            Focusable = true
        };
        button.SetTextColor(Color.ParseColor(textColor));
        button.SetBackgroundResource(backgroundResource);
        return button;
    }

    private void AddPhotoActionButton(TextView button, int widthDp)
    {
        var layout = new LinearLayout.LayoutParams(Dp(widthDp), Dp(48))
        {
            Gravity = GravityFlags.Start
        };
        layout.SetMargins(0, 0, 0, Dp(12));
        _messages?.AddView(button, layout);
    }

    private void AddSkipPhotoButton()
    {
        if (_skipReportPhotoButton is not null)
        {
            _messages?.RemoveView(_skipReportPhotoButton);
        }
        else
        {
            _skipReportPhotoButton = CreatePhotoActionButton(
                Resource.String.continue_without_photo,
                Resource.Drawable.replay_guide_button,
                "#FFF2CE");
            _skipReportPhotoButton.Click += (_, _) => ContinueWithoutPhoto();
        }

        AddPhotoActionButton(_skipReportPhotoButton, widthDp: 240);
    }

    private void ContinueWithoutPhoto()
    {
        if (!_completionPendingForPhoto)
        {
            return;
        }

        AcknowledgeRequest();
    }

    private void AcknowledgeRequest(string? emergencyAcknowledgement = null)
    {
        if (_requestAcknowledged)
        {
            return;
        }

        _requestAcknowledged = true;
        _completionPendingForPhoto = false;
        if (_reportPhotoCameraButton is not null && !_reportPhotoCaptured)
        {
            _reportPhotoCameraButton.Visibility = ViewStates.Gone;
        }

        if (_reportPhotoGalleryButton is not null && !_reportPhotoCaptured)
        {
            _reportPhotoGalleryButton.Visibility = ViewStates.Gone;
        }

        if (_skipReportPhotoButton is not null)
        {
            _skipReportPhotoButton.Visibility = ViewStates.Gone;
        }

        var acknowledgement = _isEmergency && !string.IsNullOrWhiteSpace(emergencyAcknowledgement)
            ? emergencyAcknowledgement
            : GetString(_isEmergency
                ? Resource.String.emergency_chat_confirmation
                : Resource.String.support_chat_confirmation);
        _history.Add(new TourChatMessage(false, acknowledgement));
        AddMessage(
            GetString(Resource.String.support_chat_assistant_name),
            acknowledgement,
            isUser: false);
        SetComposerEnabled(true);
    }

    private void ConfigureChatMode()
    {
        if (!_isEmergency)
        {
            return;
        }

        FindViewById<TextView>(Resource.Id.support_chat_eyebrow_text)!
            .SetText(Resource.String.emergency_chat_eyebrow);
        FindViewById<TextView>(Resource.Id.support_chat_title_text)!
            .SetText(Resource.String.emergency_chat_title);
        FindViewById<TextView>(Resource.Id.support_chat_intro_text)!
            .SetText(Resource.String.emergency_chat_intro);
        _messageInput!.Hint = GetString(Resource.String.emergency_chat_hint);
    }

    private TextView? AddMessage(string speaker, string message, bool isUser)
    {
        if (_messages is null)
        {
            return null;
        }

        var bubble = new TextView(this)
        {
            Text = $"{speaker}\n{message}",
            TextSize = 15
        };
        bubble.SetMaxWidth(Dp(320));
        bubble.SetTextColor(Color.ParseColor(isUser ? "#09251F" : "#D9E9DE"));
        bubble.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
        bubble.SetBackgroundResource(isUser
            ? Resource.Drawable.support_chat_user_bubble
            : Resource.Drawable.support_chat_assistant_bubble);

        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = isUser ? GravityFlags.End : GravityFlags.Start
        };
        layout.SetMargins(0, 0, 0, Dp(12));
        _messages.AddView(bubble, layout);

        ScrollMessagesToBottom();
        return bubble;
    }

    private void ScrollMessagesToBottom() =>
        _messagesScroll?.Post(() => _messagesScroll.FullScroll(FocusSearchDirection.Down));

    private void RemoveMessage(TextView? message)
    {
        if (message is not null)
        {
            _messages?.RemoveView(message);
        }
    }

    private void SetComposerEnabled(bool enabled)
    {
        if (_messageInput is not null)
        {
            _messageInput.Enabled = enabled;
        }

        if (_sendButton is not null)
        {
            _sendButton.Enabled = enabled;
        }
    }

    private void CloseScreen()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        HideKeyboard();
        Finish();
    }

    private void HideKeyboard()
    {
        _messageInput?.ClearFocus();
        var inputMethodManager = GetSystemService(InputMethodService) as InputMethodManager;
        inputMethodManager?.HideSoftInputFromWindow(_messageInput?.WindowToken, HideSoftInputFlags.None);
    }

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
