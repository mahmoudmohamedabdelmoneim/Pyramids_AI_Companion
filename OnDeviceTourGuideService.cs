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

        // Keep direct app/navigation answers immediate and deterministic. This was the
        // original ASK ME behavior and avoids spending a model turn on greetings, screen
        // navigation, and other facts the app already knows exactly.
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
                if (IsUsefulModelReply(qwenReply, question))
                {
                    return CompleteReply(AppTourKnowledge.ApplyCriticalPolicyGuard(question, qwenReply!));
                }
            }
        }

        // The app remains useful during first-run setup and on unsupported ABIs.
        // This deterministic fallback is local and never sends visitor data remotely.
        return CompleteReply(AppTourKnowledge.Answer(question, knowledgeStop));
    }

    private static bool IsUsefulModelReply(string? reply, string question)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        static string NormalizeForComparison(string value) =>
            new(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        return !string.Equals(
            NormalizeForComparison(reply),
            NormalizeForComparison(question),
            StringComparison.Ordinal);
    }

    private static string CompleteReply(string reply)
        => reply;
}

internal static class AppAssistantSupportRouter
{
    public static bool TryOpen(
        Context context,
        string message,
        out string reply)
    {
        var normalized = message.Trim().ToLowerInvariant();
        var isVerifiedGateIncident = VerifiedGateIncidentScenario.IsMatch(message);
        var requestsCamera = VerifiedGateIncidentScenario.RequestsCamera(message);
        var lacksReportInformation = VerifiedGateIncidentScenario.ExplicitlyLacksReportInformation(message);
        var opensCameraReport = requestsCamera || lacksReportInformation;
        var isEmergency = IsEmergencyRequest(normalized);
        var isReport = !isEmergency &&
            (isVerifiedGateIncident || opensCameraReport || IsReportRequest(normalized));
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
        reply = context.GetString(
            isVerifiedGateIncident
                ? isEmergency
                    ? Resource.String.ask_me_opening_gate_emergency_support
                    : Resource.String.ask_me_opening_gate_report_support
                : opensCameraReport
                    ? isEmergency
                        ? Resource.String.ask_me_opening_photo_emergency_support
                        : Resource.String.ask_me_opening_photo_report_support
                : isEmergency
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
        if (VerifiedGateIncidentScenario.IsSuspiciousReport(text))
        {
            return true;
        }

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

    public static bool RequiresModelCopy(Context context)
    {
        try
        {
            var applicationContext = context.ApplicationContext ?? context;
            var modelPath = GetModelPath(applicationContext);
            return string.IsNullOrWhiteSpace(modelPath) ||
                !File.Exists(modelPath) ||
                new FileInfo(modelPath).Length != ModelFileSize;
        }
        catch (Exception)
        {
            return true;
        }
    }

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
            var modelPath = GetModelPath(context);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return null;
            }

            if (File.Exists(modelPath) && new FileInfo(modelPath).Length == ModelFileSize)
            {
                return modelPath;
            }

            var modelDirectory = Path.GetDirectoryName(modelPath);
            if (string.IsNullOrWhiteSpace(modelDirectory))
            {
                return null;
            }

            Directory.CreateDirectory(modelDirectory);
            temporaryPath = modelPath + ".partial";
            // Install-time Play Asset Delivery packs are mounted as split APKs and exposed
            // through the package AssetManager. Recreating the package context ensures its
            // AssetManager includes every installed split before the large model is copied
            // to ordinary storage for the native Qwen runtime.
            using var source = OpenPackagedModel(context);
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

    private static string? GetModelPath(Context context)
    {
        var filesDirectory = context.FilesDir?.AbsolutePath;
        return string.IsNullOrWhiteSpace(filesDirectory)
            ? null
            : Path.Combine(filesDirectory, "models", ModelFileName);
    }

    private static Stream? OpenPackagedModel(Context context)
    {
        try
        {
            var packageName = context.PackageName;
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                var packageContext = context.CreatePackageContext(packageName, (PackageContextFlags)0);
                return packageContext?.Assets?.Open(ModelAssetName);
            }
        }
        catch (Exception)
        {
            // A normal APK keeps the model in the base assets. Preserve that path as a
            // fallback without adding any Play dependency to APK builds.
        }

        return context.Assets?.Open(ModelAssetName);
    }
}

internal sealed class LocalQwenRuntime
{
    private const string LogTag = "AI.Qwen";
    private const int PredictionTokenLimit = 384;
    private static readonly TimeSpan CompletionGateWait = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<string, Java.Lang.Reflect.Method> _methods = new(StringComparer.Ordinal);
    private Java.Lang.Class? _bridge;
    private bool _backendInitialized;
    private bool _nativeModelLoaded;
    private bool _modelPrepared;
    private string? _loadedModelPath;
    private bool _systemPromptPrepared;
    private string? _preparedSystemPrompt;
    private int _preparationVersion;

    public static bool IsSupported =>
        Build.SupportedAbis is not null && Build.SupportedAbis.Contains("arm64-v8a");

    public async Task<bool> PrepareAsync(
        Context context,
        string modelPath,
        TourGuideStop stop = TourGuideStop.GreatGate,
        string? systemPrompt = null)
    {
        var desiredSystemPrompt = systemPrompt ?? AppTourKnowledge.BuildHolisticSystemPrompt();
        var preparationVersion = Interlocked.Increment(ref _preparationVersion);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (preparationVersion != Volatile.Read(ref _preparationVersion))
            {
                return true;
            }

            return await Task.Run(() =>
            {
                var canResetExistingConversation =
                    _modelPrepared &&
                    _systemPromptPrepared &&
                    string.Equals(_loadedModelPath, modelPath, StringComparison.Ordinal) &&
                    string.Equals(_preparedSystemPrompt, desiredSystemPrompt, StringComparison.Ordinal);
                if (!EnsurePrepared(context, modelPath, desiredSystemPrompt))
                {
                    return false;
                }

                // Every visible chat begins with a clean native session. The caller still
                // supplies that chat's recent turns with each request, so reopening a panel
                // keeps relevant context without leaking dialogue from another screen.
                return !canResetExistingConversation || ProcessSystemPrompt(desiredSystemPrompt);
            }).ConfigureAwait(false);
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
        TourGuideStop stop = TourGuideStop.GreatGate,
        string? systemPrompt = null)
    {
        var desiredSystemPrompt = systemPrompt ?? AppTourKnowledge.BuildHolisticSystemPrompt();
        // A real send supersedes background preparations queued by older chat panels.
        Interlocked.Increment(ref _preparationVersion);
        if (!await _operationGate.WaitAsync(CompletionGateWait).ConfigureAwait(false))
        {
            Android.Util.Log.Info(
                LogTag,
                "The model is busy preparing; returning the immediate offline fallback instead of queuing the send.");
            return null;
        }

        try
        {
            return await Task.Run(() =>
            {
                if (!EnsurePrepared(context, modelPath, desiredSystemPrompt))
                {
                    Android.Util.Log.Warn(LogTag, "Completion stopped because the runtime was not prepared.");
                    return null;
                }

                var promptResult = InvokeInt(
                        "processUserPrompt",
                        new Java.Lang.String(question),
#pragma warning disable CA1422
                        new Java.Lang.Integer(PredictionTokenLimit));
#pragma warning restore CA1422
                if (promptResult != 0)
                {
                    Android.Util.Log.Warn(LogTag, $"User prompt processing failed with code {promptResult}.");
                    return null;
                }

                var reply = new StringBuilder();
                var generatedTokenCalls = 0;
                while (true)
                {
                    var token = Invoke("generateNextToken") as Java.Lang.String;
                    if (token is null)
                    {
                        break;
                    }

                    generatedTokenCalls++;
                    reply.Append(token.ToString());
                }

                Android.Util.Log.Info(
                    LogTag,
                    $"Generation completed with {generatedTokenCalls} token callbacks and {reply.Length} characters.");
                return reply.ToString().Trim();
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn(LogTag, $"Completion failed with {exception.GetType().Name}.");
            TryUnloadModel();
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private bool EnsurePrepared(Context context, string modelPath, string desiredSystemPrompt)
    {
        try
        {
            if (!IsSupported)
            {
                Android.Util.Log.Warn(LogTag, "The device ABI does not support the local model runtime.");
                return false;
            }

            // The bridge is packaged in the app's classes.dex. Class.ForName from the
            // managed runtime can resolve through the boot/Mono loader on some devices,
            // where app-defined Java classes are invisible, so always prefer the APK's
            // own class loader.
            _bridge ??= context.ClassLoader?.LoadClass("com.companyname.AndroidApp1.LocalQwenBridge")
                ?? Java.Lang.Class.ForName("com.companyname.AndroidApp1.LocalQwenBridge");
            if (!_backendInitialized)
            {
                var nativeLibraryDirectory = context.ApplicationInfo?.NativeLibraryDir;
                if (string.IsNullOrWhiteSpace(nativeLibraryDirectory))
                {
                    Android.Util.Log.Warn(LogTag, "The app native-library directory is unavailable.");
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

                var loadResult = InvokeInt("load", new Java.Lang.String(modelPath));
                if (loadResult != 0)
                {
                    Android.Util.Log.Warn(LogTag, $"Model loading failed with code {loadResult}.");
                    return false;
                }

                _nativeModelLoaded = true;
                var prepareResult = InvokeInt("prepare");
                if (prepareResult != 0)
                {
                    Android.Util.Log.Warn(LogTag, $"Model preparation failed with code {prepareResult}.");
                    TryUnloadModel();
                    return false;
                }

                _modelPrepared = true;
                _loadedModelPath = modelPath;
            }

            if (!_systemPromptPrepared ||
                !string.Equals(_preparedSystemPrompt, desiredSystemPrompt, StringComparison.Ordinal))
            {
                if (!ProcessSystemPrompt(desiredSystemPrompt))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn(LogTag, $"Runtime preparation failed with {exception.GetType().Name}.");
            TryUnloadModel();
            return false;
        }
    }

    private bool ProcessSystemPrompt(string systemPrompt)
    {
        var promptResult = InvokeInt("processSystemPrompt", new Java.Lang.String(systemPrompt));
        if (promptResult != 0)
        {
            Android.Util.Log.Warn(
                LogTag,
                $"System prompt processing failed with code {promptResult}; source length was {systemPrompt.Length} characters.");
            _systemPromptPrepared = false;
            _preparedSystemPrompt = null;
            return false;
        }

        Android.Util.Log.Info(LogTag, $"System prompt prepared from {systemPrompt.Length} characters.");
        _systemPromptPrepared = true;
        _preparedSystemPrompt = systemPrompt;
        return true;
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
        _systemPromptPrepared = false;
        _preparedSystemPrompt = null;
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
