using System;
using System.IO;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Application
{
    internal static class SessionSettingsValidator
    {
        public static SessionSettings NormalizeAndValidate(SessionSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            string sessionName = string.IsNullOrWhiteSpace(settings.SessionName)
                ? "Measurement Session"
                : settings.SessionName.Trim();

            if (settings.RollingWindowSize <= 1)
            {
                throw new ArgumentOutOfRangeException(
                    "settings",
                    "Rolling window size must be greater than 1.");
            }

            if (settings.EnterThresholdPercent <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    "settings",
                    "Enter threshold percent must be greater than 0.");
            }

            if (settings.ExitThresholdPercent <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    "settings",
                    "Exit threshold percent must be greater than 0.");
            }

            if (settings.ExitThresholdPercent < settings.EnterThresholdPercent)
            {
                throw new ArgumentException(
                    "Exit threshold percent must be greater than or equal to the enter threshold percent.",
                    "settings");
            }

            string normalizedOutputPath = NormalizeOutputPath(settings.OutputPath);

            return new SessionSettings
            {
                SessionName = sessionName,
                RollingWindowSize = settings.RollingWindowSize,
                EnterThresholdPercent = settings.EnterThresholdPercent,
                ExitThresholdPercent = settings.ExitThresholdPercent,
                OutputPath = normalizedOutputPath
            };
        }

        private static string NormalizeOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return null;
            }

            string trimmedPath = outputPath.Trim();
            string fileName = Path.GetFileName(trimmedPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Output path must include a file name.", "settings");
            }

            if (HasInvalidPathCharacters(trimmedPath))
            {
                throw new ArgumentException("Output path contains invalid characters.", "settings");
            }

            if (string.IsNullOrWhiteSpace(Path.GetExtension(trimmedPath)))
            {
                trimmedPath += ".xlsx";
            }

            return trimmedPath;
        }

        private static bool HasInvalidPathCharacters(string path)
        {
            char[] invalidChars = Path.GetInvalidPathChars();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                if (path.IndexOf(invalidChars[i]) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
