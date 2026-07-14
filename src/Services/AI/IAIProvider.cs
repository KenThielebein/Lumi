using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lumi.Services.AI
{
    public class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public interface IAIProvider
    {
        string ProviderName { get; }
        string Model { get; }
        Task<string> CompleteAsync(string userMessage, string? systemPrompt = null);
        Task<string> CompleteWithContextAsync(List<ChatMessage> history, string? systemPrompt = null);
    }
}
