using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Lumi.Config
{
    public static class AutoStartManager
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Lumi";

        public static void Apply(bool enable)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = GetCurrentExecutablePath();
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(ValueName, Quote(exePath));
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }

        public static bool IsEnabledForCurrentExecutable()
        {
            var current = GetCurrentExecutablePath();
            var registered = GetRegisteredExecutablePath();

            if (string.IsNullOrWhiteSpace(current) ||
                string.IsNullOrWhiteSpace(registered))
                return false;

            return string.Equals(
                Path.GetFullPath(current),
                Path.GetFullPath(registered),
                StringComparison.OrdinalIgnoreCase);
        }

        public static string? GetRegisteredCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) as string;
        }

        private static string? GetRegisteredExecutablePath()
        {
            var command = GetRegisteredCommand()?.Trim();
            if (string.IsNullOrWhiteSpace(command)) return null;

            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                var endQuote = command.IndexOf('"', 1);
                return endQuote > 1 ? command[1..endQuote] : null;
            }

            var firstSpace = command.IndexOf(' ');
            return firstSpace > 0 ? command[..firstSpace] : command;
        }

        private static string GetCurrentExecutablePath()
        {
            return Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? "";
        }

        private static string Quote(string path) => $"\"{path}\"";
    }
}
