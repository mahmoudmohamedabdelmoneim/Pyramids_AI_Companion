using Android.Content;
using Android.Media;
using Android.Util;
using System.Text.RegularExpressions;
using System.Threading;
using StringBuilder = System.Text.StringBuilder;
using TextEncoding = System.Text.Encoding;

namespace AndroidApp1;

internal sealed record KokoroVoiceModelPaths(
    string ModelPath,
    string VoicesPath,
    string TokensPath,
    string DataDirectory,
    string LexiconsPath,
    string RuleFstsPath);

internal static class KokoroVoiceModelInstaller
{
    private const string AssetRoot = "tts/kokoro-en-v0_19";
    private const string ModelFileName = "model.int8.onnx";
    private const string VoicesFileName = "voices.bin";
    private const string ModelCacheVersion = "kokoro-en-v0_19-int8-en-v2";
    private const long ModelFileSize = 92_361_445;
    private static readonly SemaphoreSlim CopyLock = new(1, 1);

    public static async Task<KokoroVoiceModelPaths?> TryGetPathsAsync(Context context)
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

                    var modelDirectory = Path.Combine(filesDirectory, "tts", "kokoro-en-v0_19");
                    var modelPath = Path.Combine(modelDirectory, ModelFileName);
                    var voicesPath = Path.Combine(modelDirectory, VoicesFileName);
                    var tokensPath = Path.Combine(modelDirectory, "tokens.txt");
                    var dataDirectory = Path.Combine(modelDirectory, "espeak-ng-data");
                    var readyMarker = Path.Combine(modelDirectory, ".ready");
                    var cacheIsCurrent = File.Exists(readyMarker) &&
                        string.Equals(File.ReadAllText(readyMarker).Trim(), ModelCacheVersion, StringComparison.Ordinal) &&
                        File.Exists(modelPath) &&
                        new FileInfo(modelPath).Length == ModelFileSize &&
                        File.Exists(voicesPath) &&
                        File.Exists(tokensPath) &&
                        Directory.Exists(dataDirectory);
                    if (!cacheIsCurrent)
                    {
                        CopyAssetTree(context.Assets, AssetRoot, modelDirectory);
                        if (!File.Exists(modelPath) || new FileInfo(modelPath).Length != ModelFileSize)
                        {
                            return null;
                        }

                        TryDeleteObsoleteModel(Path.Combine(modelDirectory, "model.onnx"));
                        File.WriteAllText(readyMarker, ModelCacheVersion);
                    }

                    return File.Exists(modelPath) &&
                        File.Exists(voicesPath) &&
                        File.Exists(tokensPath) &&
                        Directory.Exists(dataDirectory)
                        ? new KokoroVoiceModelPaths(
                            modelPath,
                            voicesPath,
                            tokensPath,
                            dataDirectory,
                            string.Empty,
                            string.Empty)
                        : null;
                }
                catch (Exception)
                {
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

    private static void TryDeleteObsoleteModel(string modelPath)
    {
        try
        {
            File.Delete(modelPath);
        }
        catch (IOException)
        {
            // The old cache is harmless if Android still has a transient handle to it.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep voice setup usable even if a device prevents cache cleanup.
        }
    }

}

internal sealed class LocalTourVoiceService : IDisposable
{
    private const int AudioDrainMilliseconds = 500;
    private const int MaximumCachedSegments = 6;
    private const long MaximumCachedAudioBytes = 8L * 1024 * 1024;

    public static LocalTourVoiceService Shared { get; } = new();

    private readonly object _runtimeSync = new();
    private readonly object _playbackSync = new();
    private readonly object _audioCacheSync = new();
    private readonly SemaphoreSlim _synthesisGate = new(1, 1);
    private readonly Dictionary<string, CachedTourVoiceAudio> _audioCache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _audioCacheRecency = new();
    private bool _loaded;
    private int _narrationVersion;
    private long _cachedAudioBytes;
    private AudioTrack? _activeTrack;
    private MediaPlayer? _activePlayer;
    private string? _activeAudioFile;

    public string? LastError { get; private set; }

    /// <summary>
    /// Loads the voice engine while the visitor reads the introduction screen.
    /// It intentionally creates no audio output.
    /// </summary>
    public async Task<bool> WarmUpAsync(Context context)
    {
        LastError = null;
        var model = await KokoroVoiceModelInstaller.TryGetPathsAsync(context);
        if (model is null)
        {
            LastError = "The Kokoro voice assets could not be unpacked.";
            return false;
        }

        var nativeLibraryDirectory = context.ApplicationInfo?.NativeLibraryDir;
        if (string.IsNullOrWhiteSpace(nativeLibraryDirectory))
        {
            LastError = "The installed Kokoro runtime is unavailable.";
            return false;
        }

        return await Task.Run(() => EnsureRuntimeLoaded(model, nativeLibraryDirectory));
    }

    private async Task<TourVoiceAudio?> GetCachedOrSynthesizeLatestAsync(
        KokoroVoiceModelPaths model,
        string segment,
        string nativeLibraryDirectory,
        int narrationVersion)
    {
        if (TryGetCachedAudio(segment, out var cachedAudio))
        {
            return cachedAudio;
        }

        await _synthesisGate.WaitAsync();
        try
        {
            // A native generation already in progress cannot be interrupted. Waiting callers
            // re-check their version here, so superseded narration never forms a CPU backlog.
            if (!IsCurrentNarration(narrationVersion))
            {
                return null;
            }

            if (TryGetCachedAudio(segment, out cachedAudio))
            {
                return cachedAudio;
            }

            var audio = await Task.Run(() => Synthesize(model, segment, nativeLibraryDirectory));
            if (audio is not null && IsCurrentNarration(narrationVersion))
            {
                CacheAudio(segment, audio);
            }

            return audio;
        }
        finally
        {
            _synthesisGate.Release();
        }
    }

    private bool TryGetCachedAudio(string segment, out TourVoiceAudio audio)
    {
        lock (_audioCacheSync)
        {
            if (!_audioCache.TryGetValue(segment, out var cached))
            {
                audio = null!;
                return false;
            }

            _audioCacheRecency.Remove(cached.RecencyNode);
            _audioCacheRecency.AddFirst(cached.RecencyNode);
            audio = cached.Audio;
            return true;
        }
    }

    private void CacheAudio(string segment, TourVoiceAudio audio)
    {
        var byteCount = checked(audio.Samples.LongLength * sizeof(short));
        if (byteCount > MaximumCachedAudioBytes)
        {
            return;
        }

        lock (_audioCacheSync)
        {
            if (_audioCache.TryGetValue(segment, out var existing))
            {
                _audioCacheRecency.Remove(existing.RecencyNode);
                _cachedAudioBytes -= existing.ByteCount;
            }

            var recencyNode = _audioCacheRecency.AddFirst(segment);
            _audioCache[segment] = new CachedTourVoiceAudio(audio, byteCount, recencyNode);
            _cachedAudioBytes += byteCount;

            while (_audioCache.Count > MaximumCachedSegments ||
                   _cachedAudioBytes > MaximumCachedAudioBytes)
            {
                var oldestNode = _audioCacheRecency.Last;
                if (oldestNode is null)
                {
                    break;
                }

                _audioCacheRecency.RemoveLast();
                if (_audioCache.Remove(oldestNode.Value, out var evicted))
                {
                    _cachedAudioBytes -= evicted.ByteCount;
                }
            }
        }
    }

    private void ClearAudioCache()
    {
        lock (_audioCacheSync)
        {
            _audioCache.Clear();
            _audioCacheRecency.Clear();
            _cachedAudioBytes = 0;
        }
    }

    public async Task<bool> TrySpeakAsync(Context context, string text)
    {
        LastError = null;
        if (AiSpeechPreferences.IsMuted(context))
        {
            Stop();
            return true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            LastError = "No narration text was supplied.";
            return false;
        }

        // A new narration supersedes any one that is still loading, speaking, or waiting
        // between sentences. Native generation is not interruptible, so short segments keep
        // the time until a cancellation takes effect small.
        var narrationVersion = Interlocked.Increment(ref _narrationVersion);
        ReleaseActivePlayback();

        var model = await KokoroVoiceModelInstaller.TryGetPathsAsync(context);
        if (!IsCurrentNarration(narrationVersion))
        {
            return true;
        }

        if (model is null)
        {
            LastError = "The Kokoro voice assets could not be unpacked.";
            return false;
        }

        var nativeLibraryDirectory = context.ApplicationInfo?.NativeLibraryDir;
        if (string.IsNullOrWhiteSpace(nativeLibraryDirectory))
        {
            LastError = "The installed Kokoro runtime is unavailable.";
            return false;
        }

        foreach (var segment in SplitNarration(text))

            // MediaPlayer plays each generated WAV reliably on API 36, whereas AudioTrack
            // static buffers can be rejected by the emulator audio stack during initialization.
            {
                if (!IsCurrentNarration(narrationVersion))
                {
                    return true;
                }

                var audio = await GetCachedOrSynthesizeLatestAsync(
                    model,
                    segment,
                    nativeLibraryDirectory,
                    narrationVersion);
                if (!IsCurrentNarration(narrationVersion))
                {
                    return true;
                }

                if (audio is null || audio.Samples.Length == 0)
                {
                    return false;
                }

                try
                {
                    var playback = PlayWithMediaPlayer(context, audio.Samples, audio.SampleRate);
                    Log.Info("GreatGateVoice", "Kokoro audio playback started.");
                    await Task.WhenAny(playback.Completion, Task.Delay(playback.Duration));
                    ReleaseMediaPlayback(playback.Player);
                }
                catch (Exception exception)
                {
                    SetError("Audio playback could not start", exception);
                    return false;
                }
            }

        return true;
    }

    public void Stop()
    {
        Interlocked.Increment(ref _narrationVersion);
        ReleaseActivePlayback();
    }

    private void ReleaseActiveTrack()
    {
        lock (_playbackSync)
        {
            ReleaseActiveTrackLocked();
        }
    }

    public void Dispose()
    {
        Stop();
        ClearAudioCache();
        lock (_runtimeSync)
        {
            if (_loaded)
            {
                try
                {
                    TourVoiceBridge.Unload();
                }
                catch (Exception)
                {
                    // The Android process is already disposing the native runtime.
                }
            }

            _loaded = false;
        }
    }

    private TourVoiceAudio? Synthesize(KokoroVoiceModelPaths model, string text, string nativeLibraryDirectory)
    {
        lock (_runtimeSync)
        {
            if (!EnsureRuntimeLoadedLocked(model, nativeLibraryDirectory))
            {
                return null;
            }

            try
            {
                var samples = TourVoiceBridge.Synthesize(text);
                if (samples is not { Length: > 0 })
                {
                    LastError = "The Kokoro engine returned no audio.";
                    return null;
                }

                var sampleRate = TourVoiceBridge.SampleRate;
                if (sampleRate <= 0)
                {
                    LastError = "The Kokoro engine returned an invalid sample rate.";
                    return null;
                }

                return new TourVoiceAudio(samples, sampleRate);
            }
            catch (Exception exception)
            {
                _loaded = false;
                SetError("The Kokoro native bridge failed", exception);
                return null;
            }
        }
    }

    private bool EnsureRuntimeLoaded(KokoroVoiceModelPaths model, string nativeLibraryDirectory)
    {
        lock (_runtimeSync)
        {
            return EnsureRuntimeLoadedLocked(model, nativeLibraryDirectory);
        }
    }

    private bool EnsureRuntimeLoadedLocked(KokoroVoiceModelPaths model, string nativeLibraryDirectory)
    {
        if (_loaded)
        {
            return true;
        }

        try
        {
            TourVoiceBridge.Initialize(nativeLibraryDirectory);
            if (TourVoiceBridge.Load(
                    model.ModelPath,
                    model.VoicesPath,
                    model.TokensPath,
                    model.DataDirectory,
                    model.LexiconsPath,
                    model.RuleFstsPath) != 0)
            {
                LastError = "The Kokoro engine could not load its model files.";
                return false;
            }

            _loaded = true;
            return true;
        }
        catch (Exception exception)
        {
            _loaded = false;
            SetError("The Kokoro native bridge failed", exception);
            return false;
        }
    }

    private MediaPlaybackHandle PlayWithMediaPlayer(Context context, short[] samples, int sampleRate)
    {
        ReleaseActiveMediaPlayback();
        var peakSample = samples.Max(sample => Math.Abs((int)sample));
        if (peakSample == 0)
        {
            throw new InvalidOperationException("Kokoro generated silent PCM audio.");
        }

        var attributesBuilder = new AudioAttributes.Builder();
        attributesBuilder!.SetUsage(AudioUsageKind.Media);
        attributesBuilder.SetContentType(AudioContentType.Speech);
        var attributes = attributesBuilder.Build()!;

        var audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
        if (audioManager is not null)
        {
            var focus = RequestTransientAudioFocus(audioManager, attributes);
            Log.Info("GreatGateVoice", $"Audio focus={focus}; media volume={audioManager.GetStreamVolume(Android.Media.Stream.Music)}/{audioManager.GetStreamMaxVolume(Android.Media.Stream.Music)}; peak={peakSample}.");
        }

        var audioFile = WriteWaveFile(context, NormalizeForPlayback(samples), sampleRate);
        var player = new MediaPlayer();
        try
        {
            player.SetAudioAttributes(attributes);
            player.SetDataSource(audioFile);
            player.Prepare();
            player.SetVolume(1f, 1f);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Completion += (_, _) => completion.TrySetResult(true);

            lock (_playbackSync)
            {
                _activePlayer = player;
                _activeAudioFile = audioFile;
                _activePlayer.Start();
            }

            var playbackDuration = TimeSpan.FromMilliseconds(Math.Max(
                250,
                Math.Ceiling(samples.Length * 1000d / sampleRate) + 500));
            return new MediaPlaybackHandle(player, completion.Task, playbackDuration);
        }
        catch
        {
            player.Release();
            player.Dispose();
            TryDeleteAudioFile(audioFile);
            throw;
        }
    }

    private static AudioFocusRequest RequestTransientAudioFocus(
        AudioManager audioManager,
        AudioAttributes attributes)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            using var requestBuilder = new AudioFocusRequestClass.Builder(AudioFocus.GainTransient);
            requestBuilder.SetAudioAttributes(attributes);
            using var request = requestBuilder.Build()!;
            return audioManager.RequestAudioFocus(request);
        }

        return audioManager.RequestAudioFocus(
            null,
            Android.Media.Stream.Music,
            AudioFocus.GainTransient);
    }

    private static string WriteWaveFile(Context context, short[] samples, int sampleRate)
    {
        var cacheDirectory = context.CacheDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new InvalidOperationException("Android did not provide a cache directory for Kokoro audio.");
        }

        var pcmByteCount = checked(samples.Length * sizeof(short));
        var audioFile = Path.Combine(cacheDirectory, $"kokoro-{Guid.NewGuid():N}.wav");
        using var stream = new FileStream(audioFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream, TextEncoding.ASCII, leaveOpen: false);
        writer.Write(TextEncoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked(36 + pcmByteCount));
        writer.Write(TextEncoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(checked(sampleRate * sizeof(short)));
        writer.Write((short)sizeof(short));
        writer.Write((short)(sizeof(short) * 8));
        writer.Write(TextEncoding.ASCII.GetBytes("data"));
        writer.Write(pcmByteCount);

        var pcmBytes = new byte[pcmByteCount];
        Buffer.BlockCopy(samples, 0, pcmBytes, 0, pcmByteCount);
        writer.Write(pcmBytes);
        return audioFile;
    }

    private void ReleaseMediaPlayback(MediaPlayer player)
    {
        lock (_playbackSync)
        {
            if (ReferenceEquals(_activePlayer, player))
            {
                ReleaseActiveMediaPlaybackLocked();
            }
        }
    }

    private void ReleaseActiveMediaPlayback()
    {
        lock (_playbackSync)
        {
            ReleaseActiveMediaPlaybackLocked();
        }
    }

    private void ReleaseActivePlayback()
    {
        lock (_playbackSync)
        {
            ReleaseActiveTrackLocked();
            ReleaseActiveMediaPlaybackLocked();
        }
    }

    private void ReleaseActiveMediaPlaybackLocked()
    {
        var player = _activePlayer;
        _activePlayer = null;
        if (player is not null)
        {
            try
            {
                player.Stop();
            }
            catch (Java.Lang.IllegalStateException)
            {
                // The player already completed or was released.
            }

            player.Release();
            player.Dispose();
        }

        var audioFile = _activeAudioFile;
        _activeAudioFile = null;
        TryDeleteAudioFile(audioFile);
    }

    private static void TryDeleteAudioFile(string? audioFile)
    {
        if (string.IsNullOrWhiteSpace(audioFile))
        {
            return;
        }

        try
        {
            File.Delete(audioFile);
        }
        catch (IOException)
        {
            // Android is still releasing the completed file; cache cleanup will remove it.
        }
    }

    private PlaybackHandle Play(Context context, short[] samples, int sampleRate)
    {
        ReleaseActiveTrack();
        EnsureAudibleEmulatorMusicVolume(context);
        var bufferedSamples = AddAudioDrainSilence(samples, sampleRate);
        var minimumBufferSize = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Mono, Encoding.Pcm16bit);
        var bufferSize = Math.Max(minimumBufferSize, bufferedSamples.Length * sizeof(short));

        // API 36 rejects the legacy stream-type constructor as uninitialized for this
        // 24 kHz static PCM buffer. Use the supported attributes/format builder path.
        var attributesBuilder = new AudioAttributes.Builder();
        attributesBuilder.SetUsage(AudioUsageKind.Media);
        attributesBuilder.SetContentType(AudioContentType.Speech);
        var attributes = attributesBuilder.Build()!;

        var formatBuilder = new AudioFormat.Builder();
        formatBuilder.SetEncoding(Encoding.Pcm16bit);
        formatBuilder.SetSampleRate(sampleRate);
        formatBuilder.SetChannelMask(ChannelOut.Mono);
        var format = formatBuilder.Build()!;

        var trackBuilder = new AudioTrack.Builder();
        trackBuilder.SetAudioAttributes(attributes);
        trackBuilder.SetAudioFormat(format);
        trackBuilder.SetBufferSizeInBytes(bufferSize);
        trackBuilder.SetTransferMode(AudioTrackMode.Static);
        var track = trackBuilder.Build()!;
        if (track.State != AudioTrackState.Initialized)
        {
            track.Dispose();
            throw new InvalidOperationException("Android could not initialize the Kokoro playback track.");
        }

        var written = track.Write(bufferedSamples, 0, bufferedSamples.Length);
        if (written != bufferedSamples.Length)
        {
            track.Release();
            track.Dispose();
            throw new InvalidOperationException(
                $"Android accepted only {written} of {bufferedSamples.Length} Kokoro PCM samples.");
        }

        track.SetVolume(1f);
        lock (_playbackSync)
        {
            _activeTrack = track;
            _activeTrack.Play();
        }

        var playbackDuration = TimeSpan.FromMilliseconds(Math.Max(
            250,
            Math.Ceiling(bufferedSamples.Length * 1000d / sampleRate)));
        return new PlaybackHandle(track, playbackDuration);
    }

    // The API 36 emulator restored its Music stream at 5/15 after the device reset.
    // At that index AudioFlinger applies -33 dB, which makes otherwise valid Kokoro PCM
    // effectively inaudible. Keep this diagnostic correction out of physical devices and
    // release builds; it deliberately does not alter any Kokoro model or voice setting.
    private static void EnsureAudibleEmulatorMusicVolume(Context context)
    {
#if DEBUG
        if (!string.Equals(Android.OS.Build.Hardware, "ranchu", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
        if (audioManager is null)
        {
            return;
        }

        const int audibleEmulatorMusicVolume = 12;
        var targetVolume = Math.Min(audibleEmulatorMusicVolume, audioManager.GetStreamMaxVolume(Android.Media.Stream.Music));
        if (audioManager.GetStreamVolume(Android.Media.Stream.Music) < targetVolume)
        {
            audioManager.SetStreamVolume(
                Android.Media.Stream.Music,
                targetVolume,
                (VolumeNotificationFlags)0);
            Log.Info("GreatGateVoice", $"Raised emulator Music volume to {targetVolume} for Kokoro playback.");
        }
#endif
    }

    private static short[] AddAudioDrainSilence(short[] samples, int sampleRate)
    {
        var drainSampleCount = Math.Max(1, sampleRate * AudioDrainMilliseconds / 1000);
        var paddedSamples = new short[checked(samples.Length + drainSampleCount)];
        Array.Copy(samples, paddedSamples, samples.Length);
        return paddedSamples;
    }

    private static short[] NormalizeForPlayback(short[] samples)
    {
        var peak = samples.Max(sample => Math.Abs((int)sample));
        if (peak is 0 or >= 24_000)
        {
            return samples;
        }

        // Kokoro's generated PCM can be valid but very quiet on some Android audio stacks.
        // Raise the track signal only; never change the visitor's system media volume.
        var gain = Math.Min(8d, 24_000d / peak);
        var normalized = new short[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            normalized[index] = (short)Math.Clamp(
                (int)Math.Round(samples[index] * gain),
                short.MinValue,
                short.MaxValue);
        }

        return normalized;
    }

    private bool IsCurrentNarration(int narrationVersion)
        => narrationVersion == Volatile.Read(ref _narrationVersion);

    private void ReleasePlayback(AudioTrack track)
    {
        lock (_playbackSync)
        {
            if (ReferenceEquals(_activeTrack, track))
            {
                ReleaseActiveTrackLocked();
            }
        }
    }

    private void ReleaseActiveTrackLocked()
    {
        _activeTrack?.Pause();
        _activeTrack?.Flush();
        _activeTrack?.Release();
        _activeTrack?.Dispose();
        _activeTrack = null;
    }

    private static IEnumerable<string> SplitNarration(string text)
    {
        const int maximumSegmentLength = 180;
        foreach (var sentence in Regex.Split(text.Trim(), @"(?<=[.!?])\s+|\r?\n+"))
        {
            var normalizedSentence = Regex.Replace(sentence.Trim(), @"\s+", " ");
            if (normalizedSentence.Length == 0)
            {
                continue;
            }

            var segment = new StringBuilder();
            foreach (var word in normalizedSentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.Length > 0 && segment.Length + word.Length + 1 > maximumSegmentLength)
                {
                    yield return segment.ToString();
                    segment.Clear();
                }

                if (segment.Length > 0)
                {
                    segment.Append(' ');
                }

                segment.Append(word);
            }

            if (segment.Length > 0)
            {
                yield return segment.ToString();
            }
        }
    }

    private void SetError(string context, Exception exception)
    {
        LastError = $"{context}: {exception.GetType().Name}";
        Log.Error("GreatGateVoice", exception.ToString());
    }

    private sealed record TourVoiceAudio(short[] Samples, int SampleRate);

    private sealed record CachedTourVoiceAudio(
        TourVoiceAudio Audio,
        long ByteCount,
        LinkedListNode<string> RecencyNode);

    private sealed record MediaPlaybackHandle(MediaPlayer Player, Task Completion, TimeSpan Duration);

    private sealed record PlaybackHandle(AudioTrack Track, TimeSpan Duration);
}
