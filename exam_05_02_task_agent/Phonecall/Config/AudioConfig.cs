namespace Phonecall.Config;

public class AudioConfig
{
    public string GeminiApiKey { get; set; } = "";
    public string TtsModel { get; set; } = "gemini-2.5-flash-preview-tts";
    public string SttModel { get; set; } = "gemini-2.5-flash";
    public string TtsVoice { get; set; } = "Kore";

    public string GetApiKey() =>
        !string.IsNullOrEmpty(GeminiApiKey)
            ? GeminiApiKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
}
