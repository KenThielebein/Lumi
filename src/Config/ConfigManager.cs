using System;
using System.IO;
using System.Text.Json;

namespace Lumi.Config
{
    public static class ConfigManager
    {
        public static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lumi", "config.json");

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();

            try
            {
                var json   = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new AppConfig();

                // Entschlüsseln (unterstützt auch Plaintext-Migration)
                config.GroqApiKey       = DPAPIHelper.Decrypt(config.GroqApiKey);
                config.OpenRouterApiKey = DPAPIHelper.Decrypt(config.OpenRouterApiKey);
                config.OpenAiApiKey     = DPAPIHelper.Decrypt(config.OpenAiApiKey);
                config.GitHubMemoryToken = DPAPIHelper.Decrypt(config.GitHubMemoryToken);
                config.ActiveMode = config.ActiveMode switch
                {
                    "TextEdit" or "Suggestion" => "Suggestion",
                    _ => "Dictation"
                };
                return config;
            }
            catch { return new AppConfig(); }
        }

        public static void Save(AppConfig config)
        {
            // Verschlüsseln vor dem Speichern
            var toSave = new AppConfig
            {
                GroqApiKey        = DPAPIHelper.Encrypt(config.GroqApiKey),
                OpenRouterApiKey  = DPAPIHelper.Encrypt(config.OpenRouterApiKey),
                OpenAiApiKey      = DPAPIHelper.Encrypt(config.OpenAiApiKey),
                GitHubMemoryToken = DPAPIHelper.Encrypt(config.GitHubMemoryToken),
                CloudLLMBaseUrl     = config.CloudLLMBaseUrl,
                CloudLLMModel       = config.CloudLLMModel,
                CloudSmoothingModel = config.CloudSmoothingModel,
                CustomOpenRouterModels = config.CustomOpenRouterModels,
                HiddenOpenRouterModels = config.HiddenOpenRouterModels,
                ActiveMode        = config.ActiveMode,
                STTProvider       = config.STTProvider,
                TTSProvider       = config.TTSProvider,
                Language          = config.Language,
                EnableSmoothing   = config.EnableSmoothing,
                AutoStart         = config.AutoStart,
                EnableMemory      = config.EnableMemory,
                EnableLogging     = config.EnableLogging,
                SilenceTimeoutMs  = config.SilenceTimeoutMs,
                InsertDictationImmediately = config.InsertDictationImmediately,
                GitHubMemorySyncEnabled = config.GitHubMemorySyncEnabled,
                GitHubMemoryOwner       = config.GitHubMemoryOwner,
                GitHubMemoryRepo        = config.GitHubMemoryRepo,
                GitHubMemoryBranch      = config.GitHubMemoryBranch,
                GitHubMemoryPath        = config.GitHubMemoryPath,
                OverlayTheme      = config.OverlayTheme,
                OverlayOpacity    = config.OverlayOpacity,
                OverlayLeft       = config.OverlayLeft,
                OverlayTop        = config.OverlayTop,
                OverlayWidth      = config.OverlayWidth,
                OverlayHeight     = config.OverlayHeight,
                OverlayCompact    = config.OverlayCompact,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
    }
}
