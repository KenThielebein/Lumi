using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lumi.Services.AI
{
    public class OpenRouterModel
    {
        public string Id            { get; init; } = "";
        public string Name          { get; init; } = "";
        public double PriceInPer1M  { get; init; }   // USD pro 1M Input-Token
        public double PriceOutPer1M { get; init; }   // USD pro 1M Output-Token
        public int    ContextLength { get; init; }

        public string ContextDisplay =>
            ContextLength >= 1_000_000 ? $"{ContextLength / 1_000_000}M ctx" :
            ContextLength >= 1_000     ? $"{ContextLength / 1_000}k ctx"     :
            ContextLength > 0          ? $"{ContextLength} ctx"              : "";

        public string PriceDisplay
        {
            get
            {
                if (PriceInPer1M == 0 && PriceOutPer1M == 0) return "kostenlos";
                return $"{Fmt(PriceInPer1M)} ↑  {Fmt(PriceOutPer1M)} ↓  /1M";
            }
        }

        private static string Fmt(double v) =>
            v == 0     ? "–"              :
            v < 0.001  ? $"${v:F5}"       :
            v < 0.01   ? $"${v:F4}"       :
            v < 1.0    ? $"${v:F3}"       :
                         $"${v:F2}";
    }

    public static class OpenRouterModelFetcher
    {
        private static readonly HttpClient _http = new()
            { Timeout = TimeSpan.FromSeconds(20) };

        // In-Memory-Cache: einmal pro App-Start laden
        private static List<OpenRouterModel>? _cache;

        public static async Task<List<OpenRouterModel>> GetModelsAsync(bool forceRefresh = false)
        {
            if (_cache != null && !forceRefresh) return _cache;

            var json = await _http.GetStringAsync("https://openrouter.ai/api/v1/models");
            using var doc = JsonDocument.Parse(json);

            var list = new List<OpenRouterModel>();
            foreach (var el in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id   = el.GetProperty("id").GetString() ?? "";
                var name = el.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? id : id;

                double pIn = 0, pOut = 0;
                if (el.TryGetProperty("pricing", out var pricing))
                {
                    pIn  = ParsePrice(pricing, "prompt");
                    pOut = ParsePrice(pricing, "completion");
                }

                int ctx = 0;
                if (el.TryGetProperty("context_length", out var ctxEl))
                    ctx = ctxEl.TryGetInt32(out var c) ? c : 0;

                list.Add(new OpenRouterModel
                {
                    Id            = id,
                    Name          = name,
                    PriceInPer1M  = pIn  * 1_000_000,
                    PriceOutPer1M = pOut * 1_000_000,
                    ContextLength = ctx
                });
            }

            // Sortierung: zuerst nach Name
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _cache = list;
            return list;
        }

        private static double ParsePrice(JsonElement pricing, string key)
        {
            if (!pricing.TryGetProperty(key, out var el)) return 0;
            var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
    }
}
