using System;
using System.IO;
using System.Reflection;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    public static class BeamGageRuntimeProbe
    {
        public static MeasurementSourceRuntimeProbeResult Probe()
        {
            string installPath = TryLocateInstallPath();

            if (!string.IsNullOrWhiteSpace(installPath))
            {
                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = true,
                    Summary = "BeamGage automation assemblies were detected.",
                    Details = "Install path: " + installPath
                };
            }

            string loadFailure;
            if (TryLoadAssembly("Spiricon.BeamGage.Automation", out loadFailure) ||
                TryLoadAssembly("Spiricon.Automation", out loadFailure))
            {
                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = true,
                    Summary = "BeamGage automation assemblies are loadable.",
                    Details = "Assemblies were resolved from the current machine."
                };
            }

            return new MeasurementSourceRuntimeProbeResult
            {
                DependencyAvailable = false,
                Summary = "BeamGage automation assemblies were not detected.",
                Details =
                    "Expected Spiricon automation assemblies were not found. " +
                    "Install BeamGage Professional with automation support. " +
                    (string.IsNullOrWhiteSpace(loadFailure) ? string.Empty : "Loader detail: " + loadFailure)
            };
        }

        private static string TryLocateInstallPath()
        {
            string[] roots =
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            for (int i = 0; i < roots.Length; i++)
            {
                string root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string candidate = Path.Combine(root, "Spiricon", "BeamGage Professional");
                string automationPath = Path.Combine(candidate, "Spiricon.Automation.dll");
                string beamGagePath = Path.Combine(candidate, "Spiricon.BeamGage.Automation.dll");
                if (File.Exists(automationPath) && File.Exists(beamGagePath))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool TryLoadAssembly(string assemblyName, out string failure)
        {
            try
            {
                Assembly.Load(assemblyName);
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                return false;
            }
        }
    }
}
