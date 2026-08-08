package com.companyname.AndroidApp1;

/**
 * Minimal Java-to-native bridge for the app's bundled llama.cpp runtime.
 * The methods are called from the .NET Android UI using JNI reflection.
 */
public final class LocalQwenBridge {
    static {
        System.loadLibrary("ai-chat");
    }

    private LocalQwenBridge() {
    }

    public static native void init(String nativeLibDir);
    public static native int load(String modelPath);
    public static native int prepare();
    public static native int processSystemPrompt(String systemPrompt);
    public static native int processUserPrompt(String userPrompt, int predictLength);
    public static native String generateNextToken();
    public static native void unload();
    public static native void shutdown();
}
