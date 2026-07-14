using System;

namespace Lumi.Core
{
    public enum AppMode
    {
        Suggestion,
        Dictation
    }

    public class ModeController
    {
        public AppMode CurrentMode { get; private set; } = AppMode.Dictation;

        public event EventHandler<AppMode>? ModeChanged;

        public void SetMode(AppMode mode)
        {
            if (CurrentMode == mode) return;
            CurrentMode = mode;
            ModeChanged?.Invoke(this, CurrentMode);
        }

        public void CycleMode()
        {
            CurrentMode = CurrentMode switch
            {
                AppMode.Suggestion => AppMode.Dictation,
                AppMode.Dictation  => AppMode.Suggestion,
                _                  => AppMode.Dictation
            };
            ModeChanged?.Invoke(this, CurrentMode);
        }

        public string ModeIcon => CurrentMode switch
        {
            AppMode.Suggestion => "✏️",
            AppMode.Dictation  => "🎤",
            _                  => "🎤"
        };
    }
}
