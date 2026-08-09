using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;
using Android.Views;
using Android.Widget;

namespace AndroidApp1;

/// <summary>
/// Gives every microphone in the tour the same press, hold, and release behavior.
/// Speech stays on the device when the installed Android recognizer supports it.
/// </summary>
internal sealed class HoldToTalkVoiceAssistant : Java.Lang.Object, IRecognitionListener
{
    private readonly Activity _activity;
    private readonly int _permissionRequestCode;
    private readonly TextView _status;
    private readonly LocalTourVoiceService _voiceService = LocalTourVoiceService.Shared;
    private readonly OnDeviceTourGuideService _guideService;
    private readonly List<TourChatMessage> _history;
    private readonly Func<string> _guideText;
    private readonly Func<string> _bookingText;
    private readonly Action _moveNext;
    private readonly Func<bool> _isMuted;
    private readonly Action<string, string>? _appendConversation;

    private SpeechRecognizer? _speechRecognizer;
    private bool _isActive;
    private bool _isPressed;
    private bool _isListening;
    private bool _isProcessing;
    private bool _isDestroyed;
    private string? _pendingPhrase;

    public HoldToTalkVoiceAssistant(
        Activity activity,
        int permissionRequestCode,
        ImageButton microphoneButton,
        TextView status,
        TourGuideStop stop,
        List<TourChatMessage> history,
        Func<string> guideText,
        Func<string> bookingText,
        Action moveNext,
        Func<bool> isMuted,
        Action<string, string>? appendConversation = null)
    {
        _activity = activity;
        _permissionRequestCode = permissionRequestCode;
        _status = status;
        _guideService = new OnDeviceTourGuideService(stop);
        _history = history;
        _guideText = guideText;
        _bookingText = bookingText;
        _moveNext = moveNext;
        _isMuted = isMuted;
        _appendConversation = appendConversation;

        microphoneButton.ContentDescription = activity.GetString(Resource.String.voice_assistant_mic_description);
        microphoneButton.Touch += (_, args) =>
        {
            switch (args.Event?.ActionMasked)
            {
                case MotionEventActions.Down:
                    _isPressed = true;
                    BeginListening();
                    args.Handled = true;
                    break;
                case MotionEventActions.Up:
                    _isPressed = false;
                    ReleaseAndSend();
                    args.Handled = true;
                    break;
                case MotionEventActions.Cancel:
                    _isPressed = false;
                    CancelListening();
                    args.Handled = true;
                    break;
            }
        };

        ShowHint();
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        if (!active)
        {
            CancelListening();
        }
    }

    public bool HandlePermissionResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode != _permissionRequestCode)
        {
            return false;
        }

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            _status.SetText(Resource.String.voice_assistant_microphone_ready);
        }
        else
        {
            _status.SetText(Resource.String.voice_assistant_microphone_required);
            Toast.MakeText(
                _activity,
                Resource.String.voice_assistant_microphone_required,
                ToastLength.Long)?.Show();
        }

        return true;
    }

    public void CancelListening()
    {
        _isListening = false;
        _isProcessing = false;
        _isPressed = false;
        _pendingPhrase = null;
        _speechRecognizer?.Cancel();
        if (_isActive)
        {
            ShowHint();
        }
    }

    public void Destroy()
    {
        if (_isDestroyed)
        {
            return;
        }

        _isDestroyed = true;
        _isActive = false;
        _isPressed = false;
        _isListening = false;
        _pendingPhrase = null;
        _speechRecognizer?.Cancel();
        _speechRecognizer?.Destroy();
        _speechRecognizer?.Dispose();
        _speechRecognizer = null;
    }

    private void BeginListening()
    {
        if (!_isActive || _isListening || _isProcessing || _isDestroyed ||
            _activity.IsFinishing || _activity.IsDestroyed)
        {
            return;
        }

        if (_activity.CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Permission.Granted)
        {
            _activity.RequestPermissions(
                [Android.Manifest.Permission.RecordAudio],
                _permissionRequestCode);
            return;
        }

        if (!SpeechRecognizer.IsRecognitionAvailable(_activity))
        {
            ShowUnavailable();
            return;
        }

        try
        {
            _voiceService.Stop();
            _speechRecognizer ??= CreateSpeechRecognizer();
            _pendingPhrase = null;

            var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);
            intent.PutExtra(RecognizerIntent.ExtraMaxResults, 5);
            intent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);

            _isListening = true;
            _status.SetText(Resource.String.voice_assistant_listening);
            _speechRecognizer.StartListening(intent);
        }
        catch (Exception)
        {
            _isListening = false;
            ShowUnavailable();
        }
    }

    private SpeechRecognizer CreateSpeechRecognizer()
    {
        var recognizer = SpeechRecognizer.CreateSpeechRecognizer(_activity)
            ?? throw new InvalidOperationException("Android did not provide a speech recognizer.");
        recognizer.SetRecognitionListener(this);
        return recognizer;
    }

    private void ReleaseAndSend()
    {
        if (!string.IsNullOrWhiteSpace(_pendingPhrase))
        {
            var phrase = _pendingPhrase;
            _pendingPhrase = null;
            HandleRecognizedPhrase(phrase);
            return;
        }

        if (!_isListening)
        {
            return;
        }

        _isListening = false;
        _isProcessing = true;
        _status.SetText(Resource.String.voice_assistant_processing);
        _speechRecognizer?.StopListening();
    }

    private async void HandleRecognizedPhrase(string phrase)
    {
        if (!_isActive || _isDestroyed || _activity.IsFinishing || _activity.IsDestroyed)
        {
            return;
        }

        if (IsNextCommand(phrase))
        {
            _isProcessing = false;
            _status.SetText(Resource.String.voice_assistant_next);
            _moveNext();
            return;
        }

        _isProcessing = true;
        _status.SetText(Resource.String.voice_assistant_sending);
        AddConversationMessage(true, phrase);

        try
        {
            string reply;
            if (IsBookingCommand(phrase))
            {
                reply = _bookingText();
            }
            else if (IsRepeatCommand(phrase))
            {
                reply = _guideText();
            }
            else
            {
                reply = await _guideService.AskAsync(_activity, _history, phrase);
            }

            AddConversationMessage(false, reply);
            if (!_isMuted())
            {
                if (!await _voiceService.TrySpeakAsync(_activity, reply))
                {
                    var detail = _voiceService.LastError;
                    var message = string.IsNullOrWhiteSpace(detail)
                        ? _activity.GetString(Resource.String.offline_voice_unavailable)
                        : $"{_activity.GetString(Resource.String.offline_voice_unavailable)} ({detail})";
                    Toast.MakeText(_activity, message, ToastLength.Long)?.Show();
                }
            }
        }
        catch (Exception)
        {
            var error = _activity.GetString(Resource.String.ai_chat_error_local);
            AddConversationMessage(false, error);
            Toast.MakeText(_activity, error, ToastLength.Long)?.Show();
        }
        finally
        {
            _isProcessing = false;
            if (_isActive && !_isDestroyed && !_activity.IsFinishing && !_activity.IsDestroyed)
            {
                ShowHint();
            }
        }
    }

    private void AddConversationMessage(bool isUser, string text)
    {
        _history.Add(new TourChatMessage(isUser, text));
        _appendConversation?.Invoke(
            _activity.GetString(isUser ? Resource.String.chat_you : Resource.String.ai_tour_guide_title),
            text);
    }

    private void ShowHint() => _status.SetText(Resource.String.voice_assistant_hint);

    private void ShowUnavailable()
    {
        _status.SetText(Resource.String.voice_assistant_unavailable);
        Toast.MakeText(
            _activity,
            Resource.String.voice_assistant_unavailable,
            ToastLength.Long)?.Show();
    }

    private static bool IsNextCommand(string phrase)
    {
        var command = NormalizeCommand(phrase);
        return command is "next" or "next please" or "go next" or "continue" or "continue please" or
            "move on" or "move on please";
    }

    private static bool IsBookingCommand(string phrase)
    {
        var command = NormalizeCommand(phrase);
        return command.Contains("booking", StringComparison.Ordinal) ||
            command.Contains("ticket details", StringComparison.Ordinal) ||
            command.Contains("my tickets", StringComparison.Ordinal) ||
            command.Contains("my ticket", StringComparison.Ordinal) ||
            command.Contains("access gate", StringComparison.Ordinal);
    }

    private static bool IsRepeatCommand(string phrase)
    {
        var command = NormalizeCommand(phrase);
        return command.Contains("repeat", StringComparison.Ordinal) ||
            command is "again" or "say that again" or "read this" or "read this page" or
                "read the guide" or "read this guide";
    }

    private static string NormalizeCommand(string phrase) =>
        phrase.Trim().TrimEnd('.', ',', '?', '!').Trim().ToLowerInvariant();

    public void OnReadyForSpeech(Bundle? @params) { }

    public void OnBeginningOfSpeech() { }

    public void OnRmsChanged(float rmsdB) { }

    public void OnBufferReceived(byte[]? buffer) { }

    public void OnEndOfSpeech()
    {
        if (_isActive)
        {
            _status.SetText(_isPressed
                ? Resource.String.voice_assistant_release_to_send
                : Resource.String.voice_assistant_processing);
        }
    }

    public void OnError(SpeechRecognizerError error)
    {
        _isListening = false;
        _isProcessing = false;
        if (!_isActive || _isDestroyed)
        {
            return;
        }

        if (error == SpeechRecognizerError.Client)
        {
            ShowHint();
            return;
        }

        _status.SetText(Resource.String.voice_assistant_retry);
    }

    public void OnResults(Bundle? results)
    {
        _isListening = false;
        var phrases = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)
            ?.Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .ToArray();
        if (phrases is null || phrases.Length == 0)
        {
            _isProcessing = false;
            if (_isActive)
            {
                _status.SetText(Resource.String.voice_assistant_retry);
            }
            return;
        }

        var phrase = phrases.FirstOrDefault(IsNextCommand) ??
            phrases.FirstOrDefault(IsBookingCommand) ??
            phrases.FirstOrDefault(IsRepeatCommand) ??
            phrases[0];
        if (_isPressed)
        {
            _isProcessing = false;
            _pendingPhrase = phrase;
            _status.SetText(Resource.String.voice_assistant_release_to_send);
            return;
        }

        HandleRecognizedPhrase(phrase);
    }

    public void OnPartialResults(Bundle? partialResults) { }

    public void OnEvent(int eventType, Bundle? @params) { }
}
