using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    internal static class BeamGageDataSourceSelector
    {
        private const string BeamMakerDataSource = "BeamMaker";
        private const string FileConsoleDataSource = "FileConsole";

        internal static string ResolvePhysicalDataSource(string[] availableDataSources, string configuredDataSource)
        {
            string[] physicalDataSources = GetPhysicalDataSources(availableDataSources);
            if (physicalDataSources.Length == 0)
            {
                throw new InvalidOperationException(
                    "BeamGage automation server started, but no physical data sources were detected. " +
                    "Built-in BeamMaker and FileConsole sources are not accepted in BeamGage SDK live mode.");
            }

            if (string.IsNullOrWhiteSpace(configuredDataSource))
            {
                return physicalDataSources[0];
            }

            if (!IsPhysicalDataSource(configuredDataSource))
            {
                throw new InvalidOperationException(
                    "BeamGage SDK live mode requires a physical data source. " +
                    "Configured source is built-in or invalid: " + configuredDataSource);
            }

            string selectedDataSource = ResolvePreferredItem(physicalDataSources, configuredDataSource);
            if (selectedDataSource == null)
            {
                throw new InvalidOperationException(
                    "BeamGage could not find the configured physical data source: " + configuredDataSource);
            }

            return selectedDataSource;
        }

        internal static string ResolveDataSource(
            string[] availableDataSources,
            string configuredDataSource,
            bool allowBuiltInDataSources)
        {
            if (!allowBuiltInDataSources)
            {
                return ResolvePhysicalDataSource(availableDataSources, configuredDataSource);
            }

            string[] selectableDataSources = GetSelectableDataSources(availableDataSources, true);
            if (selectableDataSources.Length == 0)
            {
                throw new InvalidOperationException("BeamGage automation server started, but no data sources were detected.");
            }

            if (string.IsNullOrWhiteSpace(configuredDataSource))
            {
                string[] physicalDataSources = GetPhysicalDataSources(availableDataSources);
                return physicalDataSources.Length > 0 ? physicalDataSources[0] : selectableDataSources[0];
            }

            string selectedDataSource = ResolvePreferredItem(selectableDataSources, configuredDataSource);
            if (selectedDataSource == null)
            {
                throw new InvalidOperationException(
                    "BeamGage could not find the configured data source: " + configuredDataSource);
            }

            return selectedDataSource;
        }

        internal static void EnsureActivePhysicalDataSource(
            string expectedDataSource,
            string currentDataSource,
            string[] availableDataSources)
        {
            EnsureActiveDataSource(expectedDataSource, currentDataSource, availableDataSources, false);
        }

        internal static void EnsureActiveDataSource(
            string expectedDataSource,
            string currentDataSource,
            string[] availableDataSources,
            bool allowBuiltInDataSources)
        {
            if (!IsPhysicalDataSource(expectedDataSource))
            {
                if (!allowBuiltInDataSources || !ContainsDataSource(availableDataSources, expectedDataSource))
                {
                    throw new InvalidOperationException(
                        "BeamGage SDK live mode does not have a valid data source selected.");
                }
            }

            if (!ContainsDataSource(availableDataSources, expectedDataSource))
            {
                throw new InvalidOperationException(
                    "BeamGage data source is no longer available: " + expectedDataSource);
            }

            if (!string.Equals(expectedDataSource, currentDataSource, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "BeamGage active data source changed unexpectedly from " +
                    expectedDataSource + " to " + (currentDataSource ?? string.Empty) + ".");
            }
        }

        internal static bool IsOnline(
            string expectedDataSource,
            string currentDataSource,
            string[] availableDataSources,
            string status)
        {
            return IsOnline(expectedDataSource, currentDataSource, availableDataSources, status, false);
        }

        internal static bool IsOnline(
            string expectedDataSource,
            string currentDataSource,
            string[] availableDataSources,
            string status,
            bool allowBuiltInDataSources)
        {
            try
            {
                EnsureActiveDataSource(expectedDataSource, currentDataSource, availableDataSources, allowBuiltInDataSources);
                return !string.Equals(status, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool ShouldPublishFrame(long frameId, long? lastPublishedFrameId)
        {
            return !lastPublishedFrameId.HasValue || frameId > lastPublishedFrameId.Value;
        }

        internal static bool IsPhysicalDataSource(string dataSource)
        {
            return !string.IsNullOrWhiteSpace(dataSource) &&
                !string.Equals(dataSource, BeamMakerDataSource, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dataSource, FileConsoleDataSource, StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] GetPhysicalDataSources(string[] availableDataSources)
        {
            if (availableDataSources == null || availableDataSources.Length == 0)
            {
                return new string[0];
            }

            List<string> physicalDataSources = new List<string>();
            for (int i = 0; i < availableDataSources.Length; i++)
            {
                string dataSource = availableDataSources[i];
                if (IsPhysicalDataSource(dataSource))
                {
                    physicalDataSources.Add(dataSource);
                }
            }

            return physicalDataSources.ToArray();
        }

        internal static string[] GetSelectableDataSources(string[] availableDataSources, bool allowBuiltInDataSources)
        {
            if (!allowBuiltInDataSources)
            {
                return GetPhysicalDataSources(availableDataSources);
            }

            if (availableDataSources == null || availableDataSources.Length == 0)
            {
                return new string[0];
            }

            List<string> selectableDataSources = new List<string>();
            for (int i = 0; i < availableDataSources.Length; i++)
            {
                string dataSource = availableDataSources[i];
                if (!string.IsNullOrWhiteSpace(dataSource))
                {
                    selectableDataSources.Add(dataSource);
                }
            }

            return selectableDataSources.ToArray();
        }

        private static bool ContainsDataSource(string[] availableDataSources, string expectedDataSource)
        {
            if (availableDataSources == null)
            {
                return false;
            }

            for (int i = 0; i < availableDataSources.Length; i++)
            {
                if (string.Equals(availableDataSources[i], expectedDataSource, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolvePreferredItem(string[] items, string preferredValue)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i], preferredValue, StringComparison.OrdinalIgnoreCase))
                {
                    return items[i];
                }
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IndexOf(preferredValue, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return items[i];
                }
            }

            return null;
        }
    }
}
