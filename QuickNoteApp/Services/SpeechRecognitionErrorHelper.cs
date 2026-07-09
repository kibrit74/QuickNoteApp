namespace QuickNoteApp.Services;

public static class SpeechRecognitionErrorHelper
{
    public const string SpeechPrivacySettingsUri = "ms-settings:privacy-speech";
    public const string MicrophoneSettingsUri = "ms-settings:privacy-microphone";
    public const string LanguageSettingsUri = "ms-settings:regionlanguage";

    public static bool IsSpeechPrivacyNotAccepted(Exception exception)
    {
        return IsSpeechPrivacyNotAccepted(exception.Message);
    }

    public static bool IsSpeechPrivacyNotAccepted(string message)
    {
        return message.Contains("speech privacy policy", StringComparison.OrdinalIgnoreCase)
            || message.Contains("konuşma gizliliği", StringComparison.OrdinalIgnoreCase)
            || message.Contains("konusma gizliligi", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTurkishSpeechLanguageMissing(Exception exception)
    {
        return exception.Message.Contains("Türkçe konuşma tanıma dili bulunamadı", StringComparison.OrdinalIgnoreCase);
    }
}
