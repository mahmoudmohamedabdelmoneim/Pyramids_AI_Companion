using Android.Runtime;

namespace AndroidApp1;

/// <summary>
/// Direct JNI access to the small Java loader packaged in local-tour-voice-bridge.jar.
/// Keeping this wrapper explicit prevents a stale generated binding from a different ABI
/// from changing the native method signatures used by the app.
/// </summary>
internal static class TourVoiceBridge
{
    private const string JavaClassName = "com/companyname/AndroidApp1/LocalTourVoiceBridge";
    private static readonly object Sync = new();
    private static IntPtr _classReference;

    public static void Initialize(string nativeLibraryDirectory)
    {
        using var directory = new Java.Lang.String(nativeLibraryDirectory);
        InvokeVoid("initialize", "(Ljava/lang/String;)V", new JValue(directory));
    }

    public static int Load(
        string modelPath,
        string voicesPath,
        string tokensPath,
        string dataDirectory,
        string lexiconsPath,
        string ruleFstsPath)
    {
        using var model = new Java.Lang.String(modelPath);
        using var voices = new Java.Lang.String(voicesPath);
        using var tokens = new Java.Lang.String(tokensPath);
        using var data = new Java.Lang.String(dataDirectory);
        using var lexicons = new Java.Lang.String(lexiconsPath);
        using var rules = new Java.Lang.String(ruleFstsPath);

        return InvokeInt(
            "load",
            "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)I",
            new JValue(model),
            new JValue(voices),
            new JValue(tokens),
            new JValue(data),
            new JValue(lexicons),
            new JValue(rules));
    }

    public static short[]? Synthesize(string text)
    {
        using var narration = new Java.Lang.String(text);
        var arrayReference = InvokeObject("synthesize", "(Ljava/lang/String;)[S", new JValue(narration));
        if (arrayReference == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var samples = new short[JNIEnv.GetArrayLength(arrayReference)];
            JNIEnv.CopyArray(arrayReference, samples);
            return samples;
        }
        finally
        {
            JNIEnv.DeleteLocalRef(arrayReference);
        }
    }

    public static int SampleRate => InvokeInt("getSampleRate", "()I");

    public static void Unload() => InvokeVoid("unload", "()V");

    public static int LoadRecognizer(string encoderPath, string decoderPath, string tokensPath)
    {
        using var encoder = new Java.Lang.String(encoderPath);
        using var decoder = new Java.Lang.String(decoderPath);
        using var tokens = new Java.Lang.String(tokensPath);
        return InvokeInt(
            "loadRecognizer",
            "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)I",
            new JValue(encoder),
            new JValue(decoder),
            new JValue(tokens));
    }

    public static string? Transcribe(short[] pcm, int sampleRate)
    {
        var audioReference = JNIEnv.NewArray(pcm);
        if (audioReference == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var resultReference = InvokeObject(
                "transcribe",
                "([SI)Ljava/lang/String;",
                new JValue(audioReference),
                new JValue(sampleRate));
            if (resultReference == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return JNIEnv.GetString(resultReference, JniHandleOwnership.DoNotTransfer);
            }
            finally
            {
                JNIEnv.DeleteLocalRef(resultReference);
            }
        }
        finally
        {
            JNIEnv.DeleteLocalRef(audioReference);
        }
    }

    public static void UnloadRecognizer() => InvokeVoid("unloadRecognizer", "()V");

    private static void InvokeVoid(string methodName, string signature, params JValue[] arguments)
    {
        lock (Sync)
        {
            JNIEnv.CallStaticVoidMethod(GetClassReference(), GetMethod(methodName, signature), arguments);
        }
    }

    private static int InvokeInt(string methodName, string signature, params JValue[] arguments)
    {
        lock (Sync)
        {
            return JNIEnv.CallStaticIntMethod(GetClassReference(), GetMethod(methodName, signature), arguments);
        }
    }

    private static IntPtr InvokeObject(string methodName, string signature, params JValue[] arguments)
    {
        lock (Sync)
        {
            return JNIEnv.CallStaticObjectMethod(GetClassReference(), GetMethod(methodName, signature), arguments);
        }
    }

    private static IntPtr GetClassReference()
    {
        if (_classReference != IntPtr.Zero)
        {
            return _classReference;
        }

        // JNIEnv.FindClass returns a global reference in .NET for Android.  Do not
        // re-wrap it as another global reference or release it as a local reference:
        // Android's checked JNI aborts the process when DeleteLocalRef receives a GREF.
        var classReference = JNIEnv.FindClass(JavaClassName);
        if (classReference == IntPtr.Zero)
        {
            throw new Java.Lang.ClassNotFoundException(JavaClassName);
        }

        _classReference = classReference;
        return _classReference;
    }

    private static IntPtr GetMethod(string methodName, string signature)
    {
        var method = JNIEnv.GetStaticMethodID(GetClassReference(), methodName, signature);
        if (method == IntPtr.Zero)
        {
            throw new MissingMethodException(JavaClassName, methodName);
        }

        return method;
    }
}
