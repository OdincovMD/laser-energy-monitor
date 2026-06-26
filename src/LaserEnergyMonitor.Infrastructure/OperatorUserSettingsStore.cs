using System;
using System.IO;
using System.Xml;

namespace LaserEnergyMonitor.Infrastructure
{
    public sealed class OperatorUserSettings
    {
        public string StarLabLogPath { get; set; }

        public string OutputPath { get; set; }

        public string SessionName { get; set; }

        public string RollingWindowSize { get; set; }

        public string EnterThresholdPercent { get; set; }

        public string ExitThresholdPercent { get; set; }

        public string BeamGageDataSource { get; set; }
    }

    public sealed class OperatorUserSettingsStore
    {
        private readonly string _settingsPath;

        public OperatorUserSettingsStore(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("Settings path is required.", "settingsPath");
            }

            _settingsPath = settingsPath;
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        public OperatorUserSettings Load()
        {
            if (!File.Exists(_settingsPath))
            {
                return new OperatorUserSettings();
            }

            XmlDocument document = new XmlDocument();
            document.Load(_settingsPath);
            XmlElement root = document.DocumentElement;
            if (root == null)
            {
                return new OperatorUserSettings();
            }

            return new OperatorUserSettings
            {
                StarLabLogPath = Read(root, "StarLabLogPath"),
                OutputPath = Read(root, "OutputPath"),
                SessionName = Read(root, "SessionName"),
                RollingWindowSize = Read(root, "RollingWindowSize"),
                EnterThresholdPercent = Read(root, "EnterThresholdPercent"),
                ExitThresholdPercent = Read(root, "ExitThresholdPercent"),
                BeamGageDataSource = Read(root, "BeamGageDataSource")
            };
        }

        public void Save(OperatorUserSettings settings)
        {
            settings = settings ?? new OperatorUserSettings();
            string directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XmlWriterSettings writerSettings = new XmlWriterSettings
            {
                Indent = true
            };

            using (XmlWriter writer = XmlWriter.Create(_settingsPath, writerSettings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("OperatorUserSettings");
                Write(writer, "StarLabLogPath", settings.StarLabLogPath);
                Write(writer, "OutputPath", settings.OutputPath);
                Write(writer, "SessionName", settings.SessionName);
                Write(writer, "RollingWindowSize", settings.RollingWindowSize);
                Write(writer, "EnterThresholdPercent", settings.EnterThresholdPercent);
                Write(writer, "ExitThresholdPercent", settings.ExitThresholdPercent);
                Write(writer, "BeamGageDataSource", settings.BeamGageDataSource);
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static string Read(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node != null ? node.InnerText : string.Empty;
        }

        private static void Write(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement(name);
            writer.WriteString(value ?? string.Empty);
            writer.WriteEndElement();
        }
    }
}
