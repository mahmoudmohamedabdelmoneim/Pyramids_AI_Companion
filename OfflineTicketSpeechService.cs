using Android.Content;
using Android.Util;
using System.Threading;

namespace AndroidApp1;

internal sealed record DistilWhisperModelPaths(string EncoderPath, string DecoderPath, string TokensPath);

/// <summary>
/// Installs the bundled, INT8 Distil-Whisper model into the app's private files directory.
/// ONNX Runtime needs normal file paths; Android assets cannot be opened directly by the model.
/// </summary>
internal static class DistilWhisperModelInstaller
{
    private const string AssetRoot = "stt/distil-small.en";
    private const string ModelCacheVersion = "distil-small-en-sherpa-int8-v1";
    private static readonly SemaphoreSlim CopyLock = new(1, 1);

    public static async Task<DistilWhisperModelPaths?> TryGetPathsAsync(Context context)
    {
        await CopyLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    var filesDirectory = context.FilesDir?.AbsolutePath;
                    if (string.IsNullOrWhiteSpace(filesDirectory) || context.Assets is null)
                    {
                        return null;
                    }

                    var modelDirectory = Path.Combine(filesDirectory, "stt", "distil-small.en");
                    var encoderPath = Path.Combine(modelDirectory, "encoder.int8.onnx");
                    var decoderPath = Path.Combine(modelDirectory, "decoder.int8.onnx");
                    var tokensPath = Path.Combine(modelDirectory, "tokens.txt");
                    var readyMarker = Path.Combine(modelDirectory, ".ready");
                    var cacheIsCurrent = File.Exists(readyMarker) &&
                        string.Equals(File.ReadAllText(readyMarker).Trim(), ModelCacheVersion, StringComparison.Ordinal) &&
                        File.Exists(encoderPath) && new FileInfo(encoderPath).Length > 0 &&
                        File.Exists(decoderPath) && new FileInfo(decoderPath).Length > 0 &&
                        File.Exists(tokensPath) && new FileInfo(tokensPath).Length > 0;
                    if (!cacheIsCurrent)
                    {
                        CopyAssetTree(context.Assets, AssetRoot, modelDirectory);
                        File.WriteAllText(readyMarker, ModelCacheVersion);
                    }

                    return File.Exists(encoderPath) && File.Exists(decoderPath) && File.Exists(tokensPath)
                        ? new DistilWhisperModelPaths(encoderPath, decoderPath, tokensPath)
                        : null;
                }
                catch (Exception exception)
                {
                    Log.Error("TicketSpeech", $"Could not install the offline speech model: {exception}");
                    return null;
                }
            });
        }
        finally
        {
            CopyLock.Release();
        }
    }

    private static void CopyAssetTree(Android.Content.Res.AssetManager assets, string assetPath, string destinationPath)
    {
        var children = assets.List(assetPath) ?? [];
        if (children.Length == 0)
        {
            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var source = assets.Open(assetPath);
            using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(destination, 1_048_576);
            return;
        }

        Directory.CreateDirectory(destinationPath);
        foreach (var child in children)
        {
            CopyAssetTree(assets, $"{assetPath}/{child}", Path.Combine(destinationPath, child));
        }
    }
}

/// <summary>
/// Local speech-to-text for the ticket screen. It never invokes Android's recognizer or a network service.
/// </summary>
internal sealed class OfflineTicketSpeechService : IDisposable
{
    public static OfflineTicketSpeechService Shared { get; } = new();

    private readonly object _runtimeSync = new();
    private bool _loaded;

    public string? LastError { get; private set; }

    /// <summary>
    /// Starts asset installation and model initialization while the microphone button is held.
    /// It is intentionally separate from transcription so release-to-response stays responsive.
    /// </summary>
    public async Task<bool> WarmUpAsync(Context context)
    {
        LastError = null;
        var model = await DistilWhisperModelInstaller.TryGetPathsAsync(context);
        if (model is null)
        {
            LastError = "The Distil-Whisper speech model could not be unpacked.";
            return false;
        }

        var nativeLibraryDirectory = context.ApplicationInfo?.NativeLibraryDir;
        if (string.IsNullOrWhiteSpace(nativeLibraryDirectory))
        {
            LastError = "The installed offline speech runtime is unavailable.";
            return false;
        }

        return await Task.Run(() => EnsureRuntimeLoaded(model, nativeLibraryDirectory));
    }

    public async Task<string?> TryTranscribeAsync(Context context, short[] pcm, int sampleRate)
    {
        LastError = null;
        if (pcm.Length < sampleRate / 2)
        {
            LastError = "The recording was too short.";
            return string.Empty;
        }

        var model = await DistilWhisperModelInstaller.TryGetPathsAsync(context);
        if (model is null)
        {
            LastError = "The Distil-Whisper speech model could not be unpacked.";
            return null;
        }

        var nativeLibraryDirectory = context.ApplicationInfo?.NativeLibraryDir;
        if (string.IsNullOrWhiteSpace(nativeLibraryDirectory))
        {
            LastError = "The installed offline speech runtime is unavailable.";
            return null;
        }

        return await Task.Run(() => Transcribe(model, nativeLibraryDirectory, pcm, sampleRate));
    }

    public void Dispose()
    {
        lock (_runtimeSync)
        {
            if (!_loaded)
            {
                return;
            }

            try
            {
                TourVoiceBridge.UnloadRecognizer();
            }
            catch (Exception)
            {
                // The Android process can already be disposing its JNI runtime.
            }
            finally
            {
                _loaded = false;
            }
        }
    }

    private string? Transcribe(
        DistilWhisperModelPaths model,
        string nativeLibraryDirectory,
        short[] pcm,
        int sampleRate)
    {
        if (!EnsureRuntimeLoaded(model, nativeLibraryDirectory))
        {
            return null;
        }

        lock (_runtimeSync)
        {
            try
            {
                return TourVoiceBridge.Transcribe(pcm, sampleRate)?.Trim();
            }
            catch (Exception exception)
            {
                _loaded = false;
                LastError = $"The offline speech bridge failed: {exception.GetType().Name}";
                Log.Error("TicketSpeech", exception.ToString());
                return null;
            }
        }
    }

    private bool EnsureRuntimeLoaded(DistilWhisperModelPaths model, string nativeLibraryDirectory)
    {
        lock (_runtimeSync)
        {
            if (_loaded)
            {
                return true;
            }

            try
            {
                TourVoiceBridge.Initialize(nativeLibraryDirectory);
                if (TourVoiceBridge.LoadRecognizer(
                        model.EncoderPath,
                        model.DecoderPath,
                        model.TokensPath) != 0)
                {
                    LastError = "The Distil-Whisper model could not be loaded.";
                    return false;
                }

                _loaded = true;
                return true;
            }
            catch (Exception exception)
            {
                _loaded = false;
                LastError = $"The offline speech bridge failed: {exception.GetType().Name}";
                Log.Error("TicketSpeech", exception.ToString());
                return false;
            }
        }
    }
}
