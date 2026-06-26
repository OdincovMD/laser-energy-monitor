using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LaserEnergyMonitor.Application;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class StarLabLogPreflightProbe : IStarLabLogPreflightProbe
    {
        public StarLabLogPreflightProbeResult Inspect(string logFilePath, string preferredEnergyColumnName)
        {
            StarLabLogPreflightProbeResult result = new StarLabLogPreflightProbeResult();
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                result.Details = "Path is empty.";
                return result;
            }

            result.FileExists = File.Exists(logFilePath);
            if (!result.FileExists)
            {
                result.Details = logFilePath;
                return result;
            }

            try
            {
                string[] header = ReadHeader(logFilePath);
                result.CanRead = true;
                if (header == null || header.Length == 0)
                {
                    result.HeaderFound = false;
                    result.Details = "No row with Timestamp header was found.";
                    return result;
                }

                result.HeaderFound = true;
                string preferred = string.IsNullOrWhiteSpace(preferredEnergyColumnName)
                    ? StarLabLogMeasurementOptions.Default.EnergyColumnName
                    : preferredEnergyColumnName.Trim();

                string resolved = ResolvePreferredColumn(header, preferred);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    result.PreferredEnergyColumnFound = true;
                    result.ResolvedEnergyColumnName = resolved;
                    result.Details = BuildColumnDetails(header);
                    return result;
                }

                resolved = ResolveFallbackColumn(header, preferred);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    result.FallbackEnergyColumnFound = true;
                    result.ResolvedEnergyColumnName = resolved;
                    result.Details = BuildColumnDetails(header);
                    return result;
                }

                result.Details = BuildColumnDetails(header);
                return result;
            }
            catch (Exception ex)
            {
                result.CanRead = false;
                result.Exception = ex;
                result.Details = ex.Message;
                return result;
            }
        }

        private static string[] ReadHeader(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.Default, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] cells = SplitColumns(line);
                    if (IndexOf(cells, "Timestamp") >= 0)
                    {
                        return cells;
                    }
                }
            }

            return new string[0];
        }

        private static string ResolvePreferredColumn(string[] columns, string preferred)
        {
            int index = IndexOf(columns, preferred);
            return index >= 0 ? columns[index] : string.Empty;
        }

        private static string ResolveFallbackColumn(string[] columns, string preferred)
        {
            string[] fallbacks =
            {
                "Math M",
                "Channel A"
            };

            for (int i = 0; i < fallbacks.Length; i++)
            {
                if (string.Equals(fallbacks[i], preferred, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int index = IndexOf(columns, fallbacks[i]);
                if (index >= 0)
                {
                    return columns[index];
                }
            }

            return string.Empty;
        }

        private static int IndexOf(string[] cells, string value)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (string.Equals(cells[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string[] SplitColumns(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new string[0];
            }

            string[] raw = line.IndexOf('\t') >= 0
                ? line.Split('\t')
                : Regex.Split(line.Trim(), @"\s{2,}");

            string[] cells = raw
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToArray();

            if (cells.Length <= 1 && line.IndexOf('\t') < 0)
            {
                cells = Regex.Split(line.Trim(), @"\s+")
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray();
            }

            return cells;
        }

        private static string BuildColumnDetails(IEnumerable<string> columns)
        {
            return "Columns: " + string.Join(", ", columns ?? new string[0]);
        }
    }
}
