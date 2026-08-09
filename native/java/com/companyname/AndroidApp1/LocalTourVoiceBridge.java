package com.companyname.AndroidApp1;

import java.io.File;

public final class LocalTourVoiceBridge {
    private static boolean initialized;

    private LocalTourVoiceBridge() {
    }

    // .NET's generated binding may initialize this class using a class loader that only
    // exposes system library directories. C# supplies the app's public nativeLibraryDir
    // before using the JNI methods, so the bundled Kokoro/Sherpa runtime is always found.
    public static synchronized void initialize(String nativeLibraryDirectory) {
        if (initialized) {
            return;
        }

        if (nativeLibraryDirectory == null || nativeLibraryDirectory.isEmpty()) {
            throw new IllegalArgumentException("The installed native library directory is unavailable.");
        }

        loadInstalledLibrary(nativeLibraryDirectory, "onnxruntime");
        loadInstalledLibrary(nativeLibraryDirectory, "sherpa-onnx-c-api");
        loadInstalledLibrary(nativeLibraryDirectory, "tour-voice");
        initialized = true;
    }

    private static void loadInstalledLibrary(String nativeLibraryDirectory, String libraryName) {
        System.load(new File(nativeLibraryDirectory, System.mapLibraryName(libraryName)).getAbsolutePath());
    }

    public static native int load(
        String modelPath,
        String voicesPath,
        String tokensPath,
        String dataDirectory,
        String lexiconsPath,
        String ruleFstsPath);
    public static native short[] synthesize(String text);
    public static native int getSampleRate();
    public static native void unload();

    // The speech recognizer shares Sherpa-ONNX and the same installed native library
    // loader as Kokoro, but it has its own model and lifetime.
    public static native int loadRecognizer(String encoderPath, String decoderPath, String tokensPath);
    public static native String transcribe(short[] pcm, int sampleRate);
    public static native void unloadRecognizer();
}
