using System.Collections.Generic;

namespace Lumi.Config
{
    public class AppConfig
    {
        // API-Keys (DPAPI-verschlüsselt gespeichert)
        public string OpenRouterApiKey { get; set; } = "";
        public string GroqApiKey       { get; set; } = "";
        public string OpenAiApiKey     { get; set; } = "";
        public string GitHubMemoryToken { get; set; } = "";

        // LLM-Konfiguration (OpenRouter Cloud)
        public string CloudLLMBaseUrl     { get; set; } = "https://openrouter.ai/api/v1";
        public string CloudLLMModel       { get; set; } = "moonshotai/kimi-k2.5";
        public string CloudSmoothingModel { get; set; } = "moonshotai/kimi-k2.5";
        public List<string> CustomOpenRouterModels { get; set; } = new();
        public List<string> HiddenOpenRouterModels { get; set; } = new();

        // STT / TTS
        public string STTProvider { get; set; } = "Groq";
        public string TTSProvider { get; set; } = "SAPI";

        // Allgemein
        public string ActiveMode     { get; set; } = "Dictation"; // Suggestion | Dictation
        public string Language       { get; set; } = "de";
        public bool   EnableSmoothing { get; set; } = true;
        public bool   AutoStart       { get; set; } = false;
        public bool   EnableMemory    { get; set; } = false;
        public bool   EnableLogging   { get; set; } = false;
        public int    SilenceTimeoutMs { get; set; } = 3000;
        public bool   InsertDictationImmediately { get; set; } = true;

        // Optionaler GitHub-Sync fuer memory.json
        public bool   GitHubMemorySyncEnabled { get; set; } = false;
        public string GitHubMemoryOwner       { get; set; } = "";
        public string GitHubMemoryRepo        { get; set; } = "";
        public string GitHubMemoryBranch      { get; set; } = "main";
        public string GitHubMemoryPath        { get; set; } = "lumi-memory.json";

        // Overlay-Erscheinungsbild
        public string OverlayTheme   { get; set; } = "Dark";   // Dark | Light | Amber | Blue
        public double OverlayOpacity { get; set; } = 0.93;

        // Overlay-Position & Größe (gespeichert beim Beenden)
        public double OverlayLeft   { get; set; } = -1;   // -1 = auto (zentriert unten)
        public double OverlayTop    { get; set; } = -1;
        public double OverlayWidth  { get; set; } = 560;
        public double OverlayHeight { get; set; } = 260;
        public bool   OverlayCompact { get; set; } = true;
    }
}
