using Android.Content;
using Android.OS;
using System.Text;

namespace AndroidApp1;

internal sealed record TourChatMessage(bool IsUser, string Text);

internal enum TourGuideStop
{
    GreatGate,
    TicketAccess,
    Checkpoint,
    ExhibitionHall,
    NextDestination,
    PanoramaStation,
    MenkaureTransfer,
    KingMenkaure,
    MenkaureHistory,
    MenkaureDeparture,
    KhafreTransfer,
    KingKhafre,
    KhafreHistory,
    KhafreSurroundings,
    KhafreCemetery,
    SphinxTransfer,
    SphinxJourney,
    SphinxStation,
    GreatSphinx,
    KhufuTransfer,
    KhufuJourney,
    KhufuStation,
    KhufuHistory,
    MeresankhTomb,
    KhufuCemeteries,
    AccessPassRefreshments,
    AccessPassTripEnd,
    PriorityPassRefreshments,
    PriorityPassTripEnd
}

internal sealed class OnDeviceTourGuideService
{
    internal static LocalQwenRuntime SharedRuntime { get; } = new();
    private readonly TourGuideStop _stop;

    public OnDeviceTourGuideService(TourGuideStop stop = TourGuideStop.GreatGate)
    {
        _stop = stop;
    }

    /// <summary>
    /// Starts model preparation after the chat panel is visible. The caller intentionally
    /// does not await this task, so opening chat and typing remain immediate while the
    /// process-wide runtime gets ready in the background.
    /// </summary>
    public async Task PrepareForChatAsync(Context context)
    {
        if (!LocalQwenRuntime.IsSupported)
        {
            return;
        }

        try
        {
            var modelPath = await QwenModelInstaller.TryGetModelPathAsync(context).ConfigureAwait(false);
            if (modelPath is not null)
            {
                await SharedRuntime.PrepareAsync(context, modelPath, _stop).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // AskAsync retains the deterministic local fallback if preparation fails.
        }
    }

    public async Task<string> AskAsync(Context context, IReadOnlyList<TourChatMessage> history, string question)
    {
        if (AppAssistantSupportRouter.TryOpen(context, question, out var supportReply))
        {
            return CompleteReply(supportReply);
        }

        var preciseNextAction = AppTourKnowledge.TryAnswerPreciseNextAction(question, _stop);
        if (preciseNextAction is not null)
        {
            return CompleteReply(preciseNextAction);
        }

        var knowledgeStop = AppTourKnowledge.ResolveKnowledgeStop(question, _stop);
        var exactAppAnswer = AppTourKnowledge.TryAnswerExactAppFact(question, knowledgeStop);
        if (exactAppAnswer is not null)
        {
            return CompleteReply(exactAppAnswer);
        }

        if (LocalQwenRuntime.IsSupported)
        {
            var modelPath = await QwenModelInstaller.TryGetModelPathAsync(context).ConfigureAwait(false);
            if (modelPath is not null)
            {
                var qwenReply = await SharedRuntime
                    .TryCompleteAsync(context, modelPath, question, _stop)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(qwenReply))
                {
                    return CompleteReply(qwenReply);
                }
            }
        }

        // The app remains useful during first-run setup and on unsupported ABIs.
        // This deterministic fallback is local and never sends visitor data remotely.
        return CompleteReply(AppTourKnowledge.Answer(question, knowledgeStop));
    }

    private static string CompleteReply(string reply)
        => reply;
}

internal static class AppAssistantSupportRouter
{
    public static bool TryOpen(Context context, string message, out string reply)
    {
        var normalized = message.Trim().ToLowerInvariant();
        var isEmergency = IsEmergencyRequest(normalized);
        var isReport = !isEmergency && IsReportRequest(normalized);
        if (!isEmergency && !isReport)
        {
            reply = string.Empty;
            return false;
        }

        var intent = new Intent(context, typeof(SupportChatActivity));
        intent.PutExtra(SupportChatActivity.EmergencyModeExtra, isEmergency);
        intent.PutExtra(SupportChatActivity.InitialMessageExtra, message.Trim());
        if (context is not Android.App.Activity)
        {
            intent.AddFlags(ActivityFlags.NewTask);
        }

        context.StartActivity(intent);
        reply = context.GetString(isEmergency
            ? Resource.String.ask_me_opening_emergency_support
            : Resource.String.ask_me_opening_report_support);
        return true;
    }

    private static bool IsEmergencyRequest(string text) =>
        ContainsAny(
            text,
            "report emergency",
            "emergency",
            "urgent help",
            "need help now",
            "ask for help",
            "i am injured",
            "i'm injured",
            "someone is injured",
            "i am hurt",
            "i'm hurt",
            "someone is hurt",
            "i feel sick",
            "i'm sick",
            "fainted",
            "unconscious",
            "not breathing",
            "fire",
            "smoke",
            "missing person",
            "lost child",
            "being threatened",
            "feel unsafe");

    private static bool IsReportRequest(string text)
    {
        if (ContainsAny(
                text,
                "something wrong",
                "report to us",
                "report a problem",
                "report an issue",
                "make a report",
                "file a report",
                "make a complaint",
                "file a complaint",
                "i want to complain",
                "overcharged",
                "charged extra",
                "refused an invoice",
                "no invoice"))
        {
            return true;
        }

        return ContainsAny(text, "seller", "provider", "saddle-man", "saddle man") &&
            ContainsAny(text, "problem", "wrong", "report", "complaint", "invoice", "charge");
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));
}

internal static class QwenModelInstaller
{
    private const string ModelAssetName = "qwen2.5-0.5b-instruct-q4_k_m.gguf";
    private const string ModelFileName = "qwen2.5-0.5b-instruct-q4_k_m.gguf";
    private const long ModelFileSize = 491_400_032;
    private static readonly object PathTaskSync = new();
    private static Task<string?>? _modelPathTask;

    public static Task<string?> TryGetModelPathAsync(Context context)
    {
        var applicationContext = context.ApplicationContext ?? context;
        lock (PathTaskSync)
        {
            return _modelPathTask ??= Task.Run(() => InstallModel(applicationContext));
        }
    }

    private static string? InstallModel(Context context)
    {
        string? temporaryPath = null;
        try
        {
            var filesDirectory = context.FilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(filesDirectory))
            {
                return null;
            }

            var modelDirectory = Path.Combine(filesDirectory, "models");
            var modelPath = Path.Combine(modelDirectory, ModelFileName);
            if (File.Exists(modelPath) && new FileInfo(modelPath).Length == ModelFileSize)
            {
                return modelPath;
            }

            Directory.CreateDirectory(modelDirectory);
            temporaryPath = modelPath + ".partial";
            using var source = context.Assets?.Open(ModelAssetName);
            if (source is null)
            {
                return null;
            }

            using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination, 1_048_576);
                destination.Flush(true);
            }

            if (new FileInfo(temporaryPath).Length != ModelFileSize)
            {
                return null;
            }

            File.Move(temporaryPath, modelPath, true);
            temporaryPath = null;
            return modelPath;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A later preparation attempt can replace an incomplete temporary file.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve fallback answers if Android is still finalizing the file.
                }
            }
        }
    }
}

internal sealed class LocalQwenRuntime
{
    private const int PredictionTokenLimit = 280;

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<string, Java.Lang.Reflect.Method> _methods = new(StringComparer.Ordinal);
    private Java.Lang.Class? _bridge;
    private bool _backendInitialized;
    private bool _nativeModelLoaded;
    private bool _modelPrepared;
    private string? _loadedModelPath;
    private bool _holisticPromptPrepared;
    private int _preparationVersion;

    public static bool IsSupported =>
        Build.SupportedAbis is not null && Build.SupportedAbis.Contains("arm64-v8a");

    public async Task<bool> PrepareAsync(
        Context context,
        string modelPath,
        TourGuideStop stop = TourGuideStop.GreatGate)
    {
        var preparationVersion = Interlocked.Increment(ref _preparationVersion);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (preparationVersion != Volatile.Read(ref _preparationVersion))
            {
                return true;
            }

            return await Task.Run(() => EnsurePrepared(context, modelPath, stop)).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<string?> TryCompleteAsync(
        Context context,
        string modelPath,
        string question,
        TourGuideStop stop = TourGuideStop.GreatGate)
    {
        // A real send supersedes background preparations queued by older chat panels.
        Interlocked.Increment(ref _preparationVersion);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (!EnsurePrepared(context, modelPath, stop))
                {
                    return null;
                }

                if (InvokeInt(
                        "processUserPrompt",
                        new Java.Lang.String(question),
#pragma warning disable CA1422
                        new Java.Lang.Integer(PredictionTokenLimit)) != 0)
#pragma warning restore CA1422
                {
                    return null;
                }

                var reply = new StringBuilder();
                while (true)
                {
                    var token = Invoke("generateNextToken") as Java.Lang.String;
                    if (token is null)
                    {
                        break;
                    }

                    reply.Append(token.ToString());
                }

                return reply.ToString().Trim();
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            TryUnloadModel();
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private bool EnsurePrepared(Context context, string modelPath, TourGuideStop stop)
    {
        try
        {
            if (!IsSupported)
            {
                return false;
            }

            _bridge ??= Java.Lang.Class.ForName("com.companyname.AndroidApp1.LocalQwenBridge");
            if (!_backendInitialized)
            {
                var nativeLibraryDirectory = context.ApplicationInfo?.NativeLibraryDir;
                if (string.IsNullOrWhiteSpace(nativeLibraryDirectory))
                {
                    return false;
                }

                InvokeVoid("init", new Java.Lang.String(nativeLibraryDirectory));
                _backendInitialized = true;
            }

            if (!_modelPrepared || !string.Equals(_loadedModelPath, modelPath, StringComparison.Ordinal))
            {
                if (_nativeModelLoaded)
                {
                    TryUnloadModel();
                }

                if (InvokeInt("load", new Java.Lang.String(modelPath)) != 0)
                {
                    return false;
                }

                _nativeModelLoaded = true;
                if (InvokeInt("prepare") != 0)
                {
                    TryUnloadModel();
                    return false;
                }

                _modelPrepared = true;
                _loadedModelPath = modelPath;
            }

            if (!_holisticPromptPrepared)
            {
                var systemPrompt = AppTourKnowledge.BuildHolisticSystemPrompt();
                if (InvokeInt("processSystemPrompt", new Java.Lang.String(systemPrompt)) != 0)
                {
                    return false;
                }

                _holisticPromptPrepared = true;
            }

            return true;
        }
        catch (Exception)
        {
            TryUnloadModel();
            return false;
        }
    }

    private void TryUnloadModel()
    {
        if (_nativeModelLoaded)
        {
            try
            {
                InvokeVoid("unload");
            }
            catch (Exception)
            {
                // Preserve the deterministic fallback after a native cleanup failure.
            }
        }

        ResetModelState();
    }

    private void ResetModelState()
    {
        _nativeModelLoaded = false;
        _modelPrepared = false;
        _loadedModelPath = null;
        _holisticPromptPrepared = false;
    }

    private void InvokeVoid(string methodName, params Java.Lang.Object[] arguments)
    {
        _ = Invoke(methodName, arguments);
    }

    private int InvokeInt(string methodName, params Java.Lang.Object[] arguments) =>
        (Invoke(methodName, arguments) as Java.Lang.Integer)?.IntValue() ?? -1;

    private Java.Lang.Object? Invoke(string methodName, params Java.Lang.Object[] arguments)
    {
        if (!_methods.TryGetValue(methodName, out var method))
        {
            method = _bridge?.GetMethods().FirstOrDefault(candidate => candidate.Name == methodName);
            if (method is not null)
            {
                _methods[methodName] = method;
            }
        }

        return method?.Invoke(null, arguments);
    }
}
