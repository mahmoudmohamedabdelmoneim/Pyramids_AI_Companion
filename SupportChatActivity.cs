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
    private const int CaptureSellerProviderPhotoRequestCode = 2401;

    private readonly OnDeviceSupportService _chatService = new();
    private readonly List<TourChatMessage> _history = new();
    private LinearLayout? _messages;
    private ScrollView? _messagesScroll;
    private EditText? _messageInput;
    private TextView? _sendButton;
    private TextView? _sellerPhotoCameraButton;
    private bool _isReplyPending;
    private bool _requestCompleted;
    private bool _isClosing;
    private bool _isEmergency;
    private bool _sellerPhotoOfferAdded;
    private bool _sellerPhotoCaptured;

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
        if (_isReplyPending || _requestCompleted)
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
            var reply = await _chatService.RespondAsync(this, _history, purpose);
            if (_isClosing)
            {
                return;
            }

            RemoveMessage(pendingMessage);
            if (reply.HasReasonableContext)
            {
                _requestCompleted = true;
                var completion = _isEmergency && !string.IsNullOrWhiteSpace(reply.Text)
                    ? reply.Text
                    : GetString(_isEmergency
                        ? Resource.String.emergency_chat_confirmation
                        : Resource.String.support_chat_confirmation);
                AddMessage(
                    GetString(Resource.String.support_chat_assistant_name),
                    completion,
                    isUser: false);
                OfferSellerPhotoCaptureIfNeeded();
                return;
            }

            _history.Add(new TourChatMessage(false, reply.Text));
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                reply.Text,
                isUser: false);
            OfferSellerPhotoCaptureIfNeeded();
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
            OfferSellerPhotoCaptureIfNeeded();
        }
        finally
        {
            _isReplyPending = false;
            if (!_isClosing)
            {
                SetComposerEnabled(!_requestCompleted);
            }
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != CaptureSellerProviderPhotoRequestCode || resultCode != Result.Ok)
        {
            return;
        }

        var photo = data?.Extras?.Get("data") as Bitmap;
        if (photo is null)
        {
            AddMessage(
                GetString(Resource.String.support_chat_assistant_name),
                GetString(Resource.String.seller_photo_capture_failed),
                isUser: false);
            return;
        }

        _sellerPhotoCaptured = true;
        if (_sellerPhotoCameraButton is not null)
        {
            _sellerPhotoCameraButton.Enabled = false;
            _sellerPhotoCameraButton.SetText(Resource.String.seller_photo_captured_action);
        }

        _history.Add(new TourChatMessage(true, "Seller/provider photo attached to the report."));
        AddCapturedPhoto(photo);
    }

    private void OfferSellerPhotoCaptureIfNeeded()
    {
        if (_isEmergency || _sellerPhotoOfferAdded || _sellerPhotoCaptured)
        {
            return;
        }

        var visitorReport = string.Join(
            " ",
            _history.Where(message => message.IsUser).Select(message => message.Text));
        if (!OnDeviceSupportService.InvoiceWasNotProvided(visitorReport.ToLowerInvariant()))
        {
            return;
        }

        _sellerPhotoOfferAdded = true;
        AddMessage(
            GetString(Resource.String.support_chat_assistant_name),
            GetString(Resource.String.seller_photo_offer),
            isUser: false);

        _sellerPhotoCameraButton = new TextView(this)
        {
            Text = GetString(Resource.String.open_camera),
            TextSize = 12,
            Gravity = GravityFlags.Center,
            Clickable = true,
            Focusable = true
        };
        _sellerPhotoCameraButton.SetTextColor(Color.ParseColor("#09251F"));
        _sellerPhotoCameraButton.SetBackgroundResource(Resource.Drawable.next_button);
        _sellerPhotoCameraButton.Click += (_, _) => OpenSellerProviderCamera();

        var layout = new LinearLayout.LayoutParams(Dp(180), Dp(48))
        {
            Gravity = GravityFlags.Start
        };
        layout.SetMargins(0, 0, 0, Dp(12));
        _messages?.AddView(_sellerPhotoCameraButton, layout);
        ScrollMessagesToBottom();
    }

    private void OpenSellerProviderCamera()
    {
        if (_sellerPhotoCaptured)
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

        StartActivityForResult(cameraIntent, CaptureSellerProviderPhotoRequestCode);
    }

    private void AddCapturedPhoto(Bitmap photo)
    {
        AddMessage(
            GetString(Resource.String.chat_you),
            GetString(Resource.String.seller_photo_attached),
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
