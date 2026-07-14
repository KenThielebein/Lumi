namespace Lumi.Services.AI
{
    public class PopularModel
    {
        public string Id          { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public bool   IsCustom    { get; init; }
        public string Category    { get; init; } = "";   // Günstig | Mittel | Premium

        // ComboBox.Text (IsEditable) = Model-ID
        public override string ToString() => Id;
    }

    public static class PopularModels
    {
        public static readonly PopularModel[] All =
        [
            // ── Günstig ───────────────────────────────────────────────────────
            new() { Id = "moonshotai/kimi-k2.5",                    DisplayName = "Kimi K2.5",             Category = "Günstig" },
            new() { Id = "google/gemini-flash-1.5",                 DisplayName = "Gemini 1.5 Flash",      Category = "Günstig" },
            new() { Id = "google/gemini-2.0-flash-001",             DisplayName = "Gemini 2.0 Flash",      Category = "Günstig" },
            new() { Id = "deepseek/deepseek-chat-v3-0324",          DisplayName = "DeepSeek V3",           Category = "Günstig" },
            new() { Id = "meta-llama/llama-3.3-70b-instruct",       DisplayName = "Llama 3.3 70B",         Category = "Günstig" },
            new() { Id = "mistralai/mistral-small-3.2-24b-instruct",DisplayName = "Mistral Small 3.2 24B", Category = "Günstig" },

            // ── Mittel ────────────────────────────────────────────────────────
            new() { Id = "openai/gpt-4o-mini",                      DisplayName = "GPT-4o Mini",           Category = "Mittel"  },
            new() { Id = "anthropic/claude-3.5-haiku",              DisplayName = "Claude 3.5 Haiku",      Category = "Mittel"  },
            new() { Id = "google/gemini-2.5-flash-preview:thinking",DisplayName = "Gemini 2.5 Flash",      Category = "Mittel"  },
            new() { Id = "x-ai/grok-3-mini-beta",                   DisplayName = "Grok 3 Mini",           Category = "Mittel"  },
            new() { Id = "mistralai/mistral-medium-3",              DisplayName = "Mistral Medium 3",      Category = "Mittel"  },

            // ── Premium ───────────────────────────────────────────────────────
            new() { Id = "openai/gpt-4o",                           DisplayName = "GPT-4o",                Category = "Premium" },
            new() { Id = "anthropic/claude-sonnet-4-5",             DisplayName = "Claude 4.5 Sonnet",     Category = "Premium" },
            new() { Id = "anthropic/claude-opus-4",                 DisplayName = "Claude 4 Opus",         Category = "Premium" },
            new() { Id = "google/gemini-2.5-pro-preview",           DisplayName = "Gemini 2.5 Pro",        Category = "Premium" },
            new() { Id = "x-ai/grok-3-beta",                        DisplayName = "Grok 3",                Category = "Premium" },
        ];
    }
}
