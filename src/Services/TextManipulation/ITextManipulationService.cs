using System;
using System.Threading.Tasks;

namespace Lumi.Services.TextManipulation
{
    public interface ITextManipulationService
    {
        /// <summary>
        /// Liest den markierten Text.
        /// Wenn <paramref name="sourceHwnd"/> angegeben, wird WM_COPY direkt ans Fenster
        /// gesendet (kein Ctrl+C, kein Win-Key-Konflikt). Fallback: Ctrl+C.
        /// </summary>
        Task<string?> GetSelectedTextAsync(IntPtr sourceHwnd = default);
        Task InsertTextAtCursorAsync(string text);
        Task<bool> ReplaceSelectionIfMatchesAsync(IntPtr sourceHwnd, string expectedText, string replacement);
        Task CopyTextAsync(string text);
    }
}
