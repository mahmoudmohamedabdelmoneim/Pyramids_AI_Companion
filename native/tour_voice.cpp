#include <jni.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <exception>
#include <mutex>
#include <string>
#include <vector>

#include "sherpa-onnx/c-api/c-api.h"

namespace {

const SherpaOnnxOfflineTts *g_tts = nullptr;
std::mutex g_tts_mutex;
int g_last_sample_rate = 22050;
const SherpaOnnxOfflineRecognizer *g_recognizer = nullptr;
std::mutex g_recognizer_mutex;

std::string GetUtf8(JNIEnv *env, jstring value) {
    if (value == nullptr) {
        return {};
    }

    const char *chars = env->GetStringUTFChars(value, nullptr);
    if (chars == nullptr) {
        return {};
    }

    std::string result(chars);
    env->ReleaseStringUTFChars(value, chars);
    return result;
}

void DestroyEngine() {
    if (g_tts != nullptr) {
        SherpaOnnxDestroyOfflineTts(g_tts);
        g_tts = nullptr;
    }
}

void DestroyRecognizer() {
    if (g_recognizer != nullptr) {
        SherpaOnnxDestroyOfflineRecognizer(g_recognizer);
        g_recognizer = nullptr;
    }
}

} // namespace

extern "C"
JNIEXPORT jint JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_load(
        JNIEnv *env,
        jclass,
        jstring jmodel_path,
        jstring jvoices_path,
        jstring jtokens_path,
        jstring jdata_dir,
        jstring jlexicons_path,
        jstring jrule_fsts_path) {
    std::lock_guard<std::mutex> lock(g_tts_mutex);
    DestroyEngine();

    const std::string model_path = GetUtf8(env, jmodel_path);
    const std::string voices_path = GetUtf8(env, jvoices_path);
    const std::string tokens_path = GetUtf8(env, jtokens_path);
    const std::string data_dir = GetUtf8(env, jdata_dir);
    const std::string lexicons_path = GetUtf8(env, jlexicons_path);
    const std::string rule_fsts_path = GetUtf8(env, jrule_fsts_path);
    if (model_path.empty() || voices_path.empty() || tokens_path.empty() || data_dir.empty()) {
        return 1;
    }

    SherpaOnnxOfflineTtsConfig config;
    memset(&config, 0, sizeof(config));
    config.model.kokoro.model = model_path.c_str();
    config.model.kokoro.voices = voices_path.c_str();
    config.model.kokoro.tokens = tokens_path.c_str();
    config.model.kokoro.data_dir = data_dir.c_str();
    if (!lexicons_path.empty()) {
        config.model.kokoro.lexicon = lexicons_path.c_str();
    }
    config.model.kokoro.lang = "";
    config.model.kokoro.length_scale = 1.0f;
    // Generate one short narration segment at a time. Four workers substantially reduce
    // the delay before the first spoken sentence without occupying the UI thread.
    config.model.num_threads = 4;
    config.model.provider = "cpu";
    config.model.debug = 0;
    if (!rule_fsts_path.empty()) {
        config.rule_fsts = rule_fsts_path.c_str();
    }
    config.max_num_sentences = 2;

    try {
        g_tts = SherpaOnnxCreateOfflineTts(&config);
        return g_tts == nullptr ? 1 : 0;
    } catch (const std::exception &) {
        // Sherpa/ONNX Runtime can throw for a damaged or incompatible asset. Never
        // allow a native exception to cross JNI and terminate the Android process.
        DestroyEngine();
        return 1;
    } catch (...) {
        DestroyEngine();
        return 1;
    }
}

extern "C"
JNIEXPORT jshortArray JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_synthesize(
        JNIEnv *env,
        jclass,
        jstring jtext) {
    std::lock_guard<std::mutex> lock(g_tts_mutex);
    if (g_tts == nullptr) {
        return nullptr;
    }

    const std::string text = GetUtf8(env, jtext);
    if (text.empty()) {
        return nullptr;
    }

    SherpaOnnxGenerationConfig generation_config;
    memset(&generation_config, 0, sizeof(generation_config));
    generation_config.sid = 0;
    generation_config.speed = 1.0f;
    generation_config.silence_scale = 0.2f;
    const SherpaOnnxGeneratedAudio *audio = nullptr;
    try {
        audio = SherpaOnnxOfflineTtsGenerateWithConfig(
                g_tts, text.c_str(), &generation_config, nullptr, nullptr);
    } catch (const std::exception &) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
    if (audio == nullptr || audio->samples == nullptr || audio->n <= 0) {
        if (audio != nullptr) {
            SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
        }
        return nullptr;
    }

    g_last_sample_rate = audio->sample_rate;
    std::vector<jshort> pcm(static_cast<size_t>(audio->n));
    for (int32_t i = 0; i < audio->n; ++i) {
        const float sample = std::clamp(audio->samples[i], -1.0f, 1.0f);
        pcm[static_cast<size_t>(i)] = static_cast<jshort>(std::lround(sample * 32767.0f));
    }

    jshortArray output = env->NewShortArray(audio->n);
    if (output != nullptr) {
        env->SetShortArrayRegion(output, 0, audio->n, pcm.data());
    }
    SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
    return output;
}

extern "C"
JNIEXPORT jint JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_getSampleRate(JNIEnv *, jclass) {
    std::lock_guard<std::mutex> lock(g_tts_mutex);
    return g_last_sample_rate;
}

extern "C"
JNIEXPORT void JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_unload(JNIEnv *, jclass) {
    std::lock_guard<std::mutex> lock(g_tts_mutex);
    DestroyEngine();
}

extern "C"
JNIEXPORT jint JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_loadRecognizer(
        JNIEnv *env,
        jclass,
        jstring jencoder_path,
        jstring jdecoder_path,
        jstring jtokens_path) {
    std::lock_guard<std::mutex> lock(g_recognizer_mutex);
    DestroyRecognizer();

    const std::string encoder_path = GetUtf8(env, jencoder_path);
    const std::string decoder_path = GetUtf8(env, jdecoder_path);
    const std::string tokens_path = GetUtf8(env, jtokens_path);
    if (encoder_path.empty() || decoder_path.empty() || tokens_path.empty()) {
        return 1;
    }

    SherpaOnnxOfflineRecognizerConfig config;
    memset(&config, 0, sizeof(config));
    config.feat_config.sample_rate = 16000;
    config.feat_config.feature_dim = 80;
    config.model_config.whisper.encoder = encoder_path.c_str();
    config.model_config.whisper.decoder = decoder_path.c_str();
    config.model_config.whisper.language = "en";
    config.model_config.whisper.task = "transcribe";
    config.model_config.tokens = tokens_path.c_str();
    config.model_config.num_threads = 4;
    config.model_config.provider = "cpu";
    config.model_config.debug = 0;
    config.decoding_method = "greedy_search";

    try {
        g_recognizer = SherpaOnnxCreateOfflineRecognizer(&config);
        return g_recognizer == nullptr ? 1 : 0;
    } catch (const std::exception &) {
        DestroyRecognizer();
        return 1;
    } catch (...) {
        DestroyRecognizer();
        return 1;
    }
}

extern "C"
JNIEXPORT jstring JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_transcribe(
        JNIEnv *env,
        jclass,
        jshortArray jpcm,
        jint sample_rate) {
    std::lock_guard<std::mutex> lock(g_recognizer_mutex);
    if (g_recognizer == nullptr || jpcm == nullptr || sample_rate <= 0) {
        return env->NewStringUTF("");
    }

    const jsize count = env->GetArrayLength(jpcm);
    if (count <= 0) {
        return env->NewStringUTF("");
    }

    std::vector<jshort> pcm(static_cast<size_t>(count));
    env->GetShortArrayRegion(jpcm, 0, count, pcm.data());
    std::vector<float> samples(static_cast<size_t>(count));
    for (jsize i = 0; i < count; ++i) {
        samples[static_cast<size_t>(i)] = static_cast<float>(pcm[static_cast<size_t>(i)]) / 32768.0f;
    }

    const SherpaOnnxOfflineStream *stream = nullptr;
    const SherpaOnnxOfflineRecognizerResult *result = nullptr;
    try {
        stream = SherpaOnnxCreateOfflineStream(g_recognizer);
        if (stream == nullptr) {
            return env->NewStringUTF("");
        }

        SherpaOnnxAcceptWaveformOffline(stream, sample_rate, samples.data(), count);
        SherpaOnnxDecodeOfflineStream(g_recognizer, stream);
        result = SherpaOnnxGetOfflineStreamResult(stream);
        const char *text = result != nullptr && result->text != nullptr ? result->text : "";
        jstring output = env->NewStringUTF(text);
        if (result != nullptr) {
            SherpaOnnxDestroyOfflineRecognizerResult(result);
        }
        SherpaOnnxDestroyOfflineStream(stream);
        return output;
    } catch (const std::exception &) {
    } catch (...) {
    }

    if (result != nullptr) {
        SherpaOnnxDestroyOfflineRecognizerResult(result);
    }
    if (stream != nullptr) {
        SherpaOnnxDestroyOfflineStream(stream);
    }
    return env->NewStringUTF("");
}

extern "C"
JNIEXPORT void JNICALL
Java_com_companyname_AndroidApp1_LocalTourVoiceBridge_unloadRecognizer(JNIEnv *, jclass) {
    std::lock_guard<std::mutex> lock(g_recognizer_mutex);
    DestroyRecognizer();
}
