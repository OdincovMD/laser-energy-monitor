using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using Microsoft.Win32;

namespace LaserEnergyMonitor.Wpf
{
    public partial class MainWindow : Window
    {
        private const int MaxEventEntries = 500;
        private const int TrendPointCapacity = 90;
        private readonly MeasurementSessionRuntimeFactory _runtimeFactory;
        private readonly string _defaultOutputDir;
        private readonly List<double> _beamEnergyTrend = new List<double>();
        private readonly List<double> _ophirEnergyTrend = new List<double>();
        private readonly List<double> _stabilityTrend = new List<double>();
        private readonly List<string> _eventLines = new List<string>();
        private MeasurementSessionService _service;
        private string _activeFirstSourceKey;
        private string _activeSecondSourceKey;
        private int _stationaryEntries;
        private bool _isBindingStartupData;

        public MainWindow(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;

            InitializeComponent();
            BindStartupData();
            UpdateState(MeasurementSessionState.Idle);
            ResetLiveValues();
            ResetSessionReview();
            RefreshSourceDiagnostics();
            AddEvent("Application ready.");
        }

        protected override void OnClosed(EventArgs e)
        {
            ReplaceService(null);
            base.OnClosed(e);
        }

        private void BindStartupData()
        {
            _isBindingStartupData = true;
            try
            {
                BeamSourceComboBox.ItemsSource = _runtimeFactory.FirstSourceOptions;
                OphirSourceComboBox.ItemsSource = _runtimeFactory.SecondSourceOptions;
                SelectSource(BeamSourceComboBox, "beam-sim");
                SelectSource(OphirSourceComboBox, "ophir-sim");

                PolicyComboBox.ItemsSource = new[]
                {
                    new PolicyOption("Fault session", DesynchronizationPolicyAction.FaultSession),
                    new PolicyOption("Stop gracefully", DesynchronizationPolicyAction.StopGracefully),
                    new PolicyOption("Log only", DesynchronizationPolicyAction.LogOnly)
                };
                PolicyComboBox.SelectedIndex = 0;

                OutputPathTextBox.Text = Path.Combine(_defaultOutputDir, "measurement-session.xlsx");
                OutputPathTextBox.ToolTip = OutputPathTextBox.Text;
            }
            finally
            {
                _isBindingStartupData = false;
            }
        }

        private static void SelectSource(System.Windows.Controls.ComboBox comboBox, string key)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                MeasurementSourceOption option = comboBox.Items[i] as MeasurementSourceOption;
                if (option != null && string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void OnInitializeClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Initialization error",
                delegate
                {
                    EnsureInitializedService(BuildSettings(), true);
                    AddEvent("Sources initialized.");
                });
        }

        private void OnStartClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Start error",
                delegate
                {
                    MeasurementSessionService service = EnsureInitializedService(BuildSettings(), false);
                    service.Start();
                    AddEvent("Session started.");
                });
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Stop error",
                delegate
                {
                    if (_service == null)
                    {
                        return;
                    }

                    _service.Stop();
                    AddEvent("Session stopped.");
                    ResetLiveValues();
                });
        }

        private void OnSelfTestClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Self-test error",
                delegate
                {
                    string report = _runtimeFactory.RunSelfTest(GetSelectedSourceKey(BeamSourceComboBox), GetSelectedSourceKey(OphirSourceComboBox));
                    DiagnosticsReportTextBox.Text = report;
                    RefreshSourceDiagnostics();
                    AddEvent("Hardware self-test completed.");
                    MessageBox.Show(this, report, "Hardware Self-Test", MessageBoxButton.OK, MessageBoxImage.Information);
                });
        }

        private void OnBeamGageSmokeTestClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "BeamGage smoke-test error",
                delegate
                {
                    string report = _runtimeFactory.RunBeamGageSmokeTest(GetSelectedSourceKey(BeamSourceComboBox));
                    DiagnosticsReportTextBox.Text = report;
                    AddEvent("BeamGage smoke-test completed.");
                    MessageBox.Show(this, report, "BeamGage Smoke-Test", MessageBoxButton.OK, MessageBoxImage.Information);
                });
        }

        private void OnOphirSmokeTestClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Ophir smoke-test error",
                delegate
                {
                    string report = _runtimeFactory.RunOphirSmokeTest(GetSelectedSourceKey(OphirSourceComboBox));
                    DiagnosticsReportTextBox.Text = report;
                    AddEvent("Ophir smoke-test completed.");
                    MessageBox.Show(this, report, "Ophir Smoke-Test", MessageBoxButton.OK, MessageBoxImage.Information);
                });
        }

        private void OnBrowseClicked(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*";
            dialog.Title = "Select export file";
            dialog.FileName = Path.GetFileName(OutputPathTextBox.Text);
            string directory = Path.GetDirectoryName(OutputPathTextBox.Text);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog(this) == true)
            {
                OutputPathTextBox.Text = dialog.FileName;
                OutputPathTextBox.ToolTip = dialog.FileName;
            }
        }

        private void OnClearEventsClicked(object sender, RoutedEventArgs e)
        {
            _eventLines.Clear();
            if (EventsTextBox != null)
            {
                EventsTextBox.Clear();
            }
        }

        private void OnSourceSelectionChanged(object sender, EventArgs e)
        {
            if (_isBindingStartupData)
            {
                return;
            }

            RefreshSourceDiagnostics();
        }

        private void RunUiAction(string title, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private MeasurementSessionService EnsureInitializedService(SessionSettings settings, bool forceRecreate)
        {
            string firstKey = GetSelectedSourceKey(BeamSourceComboBox);
            string secondKey = GetSelectedSourceKey(OphirSourceComboBox);
            bool selectionChanged =
                !string.Equals(firstKey, _activeFirstSourceKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(secondKey, _activeSecondSourceKey, StringComparison.OrdinalIgnoreCase);

            if (_service != null && IsSessionConfigurationLocked(_service.State))
            {
                throw new InvalidOperationException("Stop the active measurement session before reconfiguring sources or session settings.");
            }

            if (!forceRecreate && !selectionChanged && _service != null && _service.State == MeasurementSessionState.Initialized)
            {
                return _service;
            }

            MeasurementSessionService newService = _runtimeFactory.Create(firstKey, secondKey);
            try
            {
                AttachServiceEvents(newService);
                newService.Initialize(settings);
                ReplaceService(newService);
                ResetSessionReview();
                _activeFirstSourceKey = firstKey;
                _activeSecondSourceKey = secondKey;
                UpdateState(_service.State);
                return _service;
            }
            catch
            {
                newService.Dispose();
                throw;
            }
        }

        private void AttachServiceEvents(MeasurementSessionService service)
        {
            service.StateChanged += OnServiceStateChanged;
            service.LiveMeasurementUpdated += OnLiveMeasurementUpdated;
            service.SessionEventRaised += OnSessionEventRaised;
            service.SessionSummaryAvailable += OnSessionSummaryAvailable;
        }

        private void ReplaceService(MeasurementSessionService newService)
        {
            if (_service != null)
            {
                _service.StateChanged -= OnServiceStateChanged;
                _service.LiveMeasurementUpdated -= OnLiveMeasurementUpdated;
                _service.SessionEventRaised -= OnSessionEventRaised;
                _service.SessionSummaryAvailable -= OnSessionSummaryAvailable;
                _service.Dispose();
            }

            _service = newService;
            if (_service == null)
            {
                ResetLiveValues();
                ResetSessionReview();
            }
        }

        private void OnServiceStateChanged(object sender, SessionStateChangedEventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(delegate { UpdateState(args.State); }));
        }

        private void OnLiveMeasurementUpdated(object sender, LiveMeasurementUpdatedEventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(delegate { UpdateLiveValues(args.Snapshot); }));
        }

        private void OnTrendPlotSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawTrendStrip();
        }

        private void OnTrendPlotLoaded(object sender, RoutedEventArgs e)
        {
            RedrawTrendStrip();
        }

        private void OnSessionEventRaised(object sender, SessionEventRaisedEventArgs args)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    delegate
                    {
                        SessionEvent sessionEvent = args != null ? args.SessionEvent : null;
                        if (sessionEvent != null && sessionEvent.EventType == SessionEventType.StationaryEntered)
                        {
                            _stationaryEntries += 1;
                            StationaryEntriesText.Text = _stationaryEntries.ToString(CultureInfo.InvariantCulture);
                        }

                        AddEvent(FormatEvent(sessionEvent));
                    }));
        }

        private void OnSessionSummaryAvailable(object sender, SessionSummaryAvailableEventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(delegate { UpdateSummary(args != null ? args.Summary : null); }));
        }

        private SessionSettings BuildSettings()
        {
            return new SessionSettings
            {
                SessionName = SessionNameTextBox.Text,
                RollingWindowSize = ParseInt(WindowSizeTextBox.Text, 20),
                EnterThresholdPercent = ParseDouble(EnterThresholdTextBox.Text, 0.5d),
                ExitThresholdPercent = ParseDouble(ExitThresholdTextBox.Text, 1.0d),
                SynchronizationDelta = TimeSpan.FromMilliseconds(ParseDouble(SyncDeltaTextBox.Text, 10.0d)),
                MaxConsecutiveDesynchronizations = ParseInt(DesyncLimitTextBox.Text, 3),
                DesynchronizationPolicyAction = GetSelectedPolicy(),
                OutputPath = OutputPathTextBox.Text
            };
        }

        private DesynchronizationPolicyAction GetSelectedPolicy()
        {
            PolicyOption option = PolicyComboBox.SelectedItem as PolicyOption;
            return option != null ? option.Action : DesynchronizationPolicyAction.FaultSession;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static string GetSelectedSourceKey(System.Windows.Controls.ComboBox comboBox)
        {
            MeasurementSourceOption option = comboBox != null ? comboBox.SelectedItem as MeasurementSourceOption : null;
            return option != null ? option.Key : string.Empty;
        }

        private void RefreshSourceDiagnostics()
        {
            if (BeamSourceComboBox == null || OphirSourceComboBox == null || DiagnosticsReportTextBox == null)
            {
                return;
            }

            string firstKey = GetSelectedSourceKey(BeamSourceComboBox);
            string secondKey = GetSelectedSourceKey(OphirSourceComboBox);
            if (string.IsNullOrWhiteSpace(firstKey) || string.IsNullOrWhiteSpace(secondKey))
            {
                return;
            }

            DiagnosticsReportTextBox.Text = _runtimeFactory.BuildDiagnostics(firstKey, secondKey);

            var diagnostics = _runtimeFactory.GetDiagnostics(firstKey, secondKey);
            if (diagnostics.Count > 0)
            {
                BindDiagnosticCard(
                    diagnostics[0],
                    BeamDiagnosticTitleText,
                    BeamDiagnosticSummaryText,
                    BeamAcquisitionText,
                    BeamDiagnosticStepsText,
                    BeamDiagnosticDetailsText);
            }

            if (diagnostics.Count > 1)
            {
                BindDiagnosticCard(
                    diagnostics[1],
                    OphirDiagnosticTitleText,
                    OphirDiagnosticSummaryText,
                    OphirAcquisitionText,
                    OphirDiagnosticStepsText,
                    OphirDiagnosticDetailsText);
            }
        }

        private void UpdateState(MeasurementSessionState state)
        {
            bool locked = IsSessionConfigurationLocked(state);
            StartButton.IsEnabled =
                state == MeasurementSessionState.Idle ||
                state == MeasurementSessionState.Initialized ||
                state == MeasurementSessionState.Completed;
            StopButton.IsEnabled =
                state == MeasurementSessionState.Measuring ||
                state == MeasurementSessionState.Stationary;
            InitializeButton.IsEnabled = !locked;
            SelfTestButton.IsEnabled = !locked;
            BeamGageTestButton.IsEnabled = !locked;
            OphirTestButton.IsEnabled = !locked;
            BeamSourceComboBox.IsEnabled = !locked;
            OphirSourceComboBox.IsEnabled = !locked;
            SessionNameTextBox.IsReadOnly = locked;
            WindowSizeTextBox.IsReadOnly = locked;
            EnterThresholdTextBox.IsReadOnly = locked;
            ExitThresholdTextBox.IsReadOnly = locked;
            SyncDeltaTextBox.IsReadOnly = locked;
            DesyncLimitTextBox.IsReadOnly = locked;
            PolicyComboBox.IsEnabled = !locked;
            OutputPathTextBox.IsReadOnly = locked;
            BrowseButton.IsEnabled = !locked;
            if (state == MeasurementSessionState.Idle || state == MeasurementSessionState.Initialized)
            {
                ResetLiveValues();
            }
        }

        private Brush GetBrush(string resourceKey)
        {
            return (Brush)FindResource(resourceKey);
        }

        private static bool IsSessionConfigurationLocked(MeasurementSessionState state)
        {
            return state == MeasurementSessionState.Measuring || state == MeasurementSessionState.Stationary;
        }

        private void UpdateLiveValues(LiveMeasurementSnapshot snapshot)
        {
            PairIdText.Text = snapshot.PairId.ToString(CultureInfo.InvariantCulture);
            LastUpdateText.Text = snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            BeamEnergyText.Text = FormatDouble(snapshot.FirstEnergy);
            OphirEnergyText.Text = FormatDouble(snapshot.SecondEnergy);
            BeamAverageText.Text = FormatDouble(snapshot.FirstAverage);
            OphirAverageText.Text = FormatDouble(snapshot.SecondAverage);
            StabilityText.Text = FormatDouble(snapshot.StabilityMetric);
            AddTrendPoint(_beamEnergyTrend, snapshot.FirstEnergy);
            AddTrendPoint(_ophirEnergyTrend, snapshot.SecondEnergy);
            AddTrendPoint(_stabilityTrend, snapshot.StabilityMetric);
            RedrawTrendStrip();
        }

        private void ResetLiveValues()
        {
            PairIdText.Text = "-";
            LastUpdateText.Text = "-";
            BeamEnergyText.Text = "-";
            OphirEnergyText.Text = "-";
            BeamAverageText.Text = "-";
            OphirAverageText.Text = "-";
            StabilityText.Text = "-";
            _stationaryEntries = 0;
            StationaryEntriesText.Text = "0";
            _beamEnergyTrend.Clear();
            _ophirEnergyTrend.Clear();
            _stabilityTrend.Clear();
            RedrawTrendStrip();
        }

        private static void AddTrendPoint(List<double> trend, double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                return;
            }

            trend.Add(value.Value);
            while (trend.Count > TrendPointCapacity)
            {
                trend.RemoveAt(0);
            }
        }

        private void RedrawTrendStrip()
        {
            double width = TrendPlotCanvas.ActualWidth;
            double height = TrendPlotCanvas.ActualHeight;
            if (width <= 1.0d || height <= 1.0d)
            {
                return;
            }

            DrawTrendLine(BeamTrendLine, _beamEnergyTrend, width, height);
            DrawTrendLine(OphirTrendLine, _ophirEnergyTrend, width, height);
            DrawTrendLine(StabilityTrendLine, _stabilityTrend, width, height);
            TrendBeamText.Text = BuildTrendLabel("Beam", _beamEnergyTrend);
            TrendOphirText.Text = BuildTrendLabel("Ophir", _ophirEnergyTrend);
            TrendStabilityText.Text = BuildTrendLabel("Stability", _stabilityTrend);

            int sampleCount = Math.Max(_beamEnergyTrend.Count, Math.Max(_ophirEnergyTrend.Count, _stabilityTrend.Count));
            TrendRangeText.Text = sampleCount == 0
                ? "Waiting for samples"
                : sampleCount.ToString(CultureInfo.InvariantCulture) + " samples";
        }

        private static void DrawTrendLine(System.Windows.Shapes.Polyline line, IReadOnlyList<double> values, double width, double height)
        {
            PointCollection points = new PointCollection();
            if (values.Count == 0)
            {
                line.Points = points;
                return;
            }

            double min = values[0];
            double max = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                min = Math.Min(min, values[i]);
                max = Math.Max(max, values[i]);
            }

            double range = max - min;
            if (range < 0.000001d)
            {
                range = 1.0d;
                min -= 0.5d;
            }

            if (values.Count == 1)
            {
                double normalizedSingle = (values[0] - min) / range;
                double singleY = height - (normalizedSingle * (height - 8.0d)) - 4.0d;
                double centerX = width / 2.0d;
                points.Add(new Point(Math.Max(0.0d, centerX - 8.0d), singleY));
                points.Add(new Point(Math.Min(width, centerX + 8.0d), singleY));
                line.Points = points;
                return;
            }

            double step = width / (values.Count - 1);
            for (int i = 0; i < values.Count; i++)
            {
                double normalized = (values[i] - min) / range;
                double x = i * step;
                double y = height - (normalized * (height - 8.0d)) - 4.0d;
                points.Add(new Point(x, y));
            }

            line.Points = points;
        }

        private static string BuildTrendLabel(string label, IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return label + " --";
            }

            double latest = values[values.Count - 1];
            return label + " " + latest.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private void ResetSessionReview()
        {
            SummaryPairsText.Text = "0";
            SummaryEventsCountText.Text = "0";
            SummarySegmentsText.Text = "0";
            SummaryFaultsText.Text = "0";
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "-";
        }

        private void AddEvent(string message)
        {
            _eventLines.Insert(0, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message);
            while (_eventLines.Count > MaxEventEntries)
            {
                _eventLines.RemoveAt(_eventLines.Count - 1);
            }

            if (EventsTextBox != null)
            {
                EventsTextBox.Text = string.Join(Environment.NewLine, _eventLines);
                EventsTextBox.ScrollToHome();
            }
        }

        private static string FormatEvent(SessionEvent sessionEvent)
        {
            if (sessionEvent == null)
            {
                return "Event data unavailable.";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss} [{1}] {2}",
                sessionEvent.TimestampUtc.ToLocalTime(),
                sessionEvent.EventType,
                sessionEvent.Message);
        }

        private void UpdateSummary(SessionSummary summary)
        {
            if (summary == null)
            {
                ResetSessionReview();
                return;
            }

            SummaryPairsText.Text = summary.PairCount.ToString(CultureInfo.InvariantCulture);
            SummaryEventsCountText.Text = summary.EventCount.ToString(CultureInfo.InvariantCulture);
            SummarySegmentsText.Text = summary.ClosedStationarySegmentCount.ToString(CultureInfo.InvariantCulture);
            SummaryFaultsText.Text = summary.FaultCount.ToString(CultureInfo.InvariantCulture);
        }

        private void BindDiagnosticCard(
            MeasurementSourceDiagnostic diagnostic,
            System.Windows.Controls.TextBlock titleText,
            System.Windows.Controls.TextBlock summaryText,
            System.Windows.Controls.TextBlock acquisitionText,
            System.Windows.Controls.TextBox stepsText,
            System.Windows.Controls.TextBox detailsText)
        {
            MeasurementSourceRuntimeProbeResult probe = diagnostic != null ? diagnostic.Probe : null;
            bool dependencyAvailable = probe != null && probe.DependencyAvailable;

            titleText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1}",
                diagnostic != null ? diagnostic.SlotName : "Source",
                diagnostic != null ? diagnostic.DisplayName : "Unavailable");
            titleText.Foreground = dependencyAvailable ? GetBrush("StatusSuccessTextBrush") : GetBrush("StatusDangerTextBrush");
            summaryText.Text = probe != null ? probe.Summary : "No diagnostic data available.";
            acquisitionText.Text =
                diagnostic != null && diagnostic.IsImplemented
                    ? "Acquisition available in this build."
                    : "Acquisition not wired in this build.";

            stepsText.Text = BuildStepsText(probe);
            detailsText.Text = probe != null && !string.IsNullOrWhiteSpace(probe.Details)
                ? probe.Details
                : "No additional details were reported.";
        }

        private static string BuildStepsText(MeasurementSourceRuntimeProbeResult probe)
        {
            if (probe == null || probe.Steps == null || probe.Steps.Count == 0)
            {
                return "No probe steps were reported.";
            }

            List<string> lines = new List<string>(probe.Steps.Count);
            for (int i = 0; i < probe.Steps.Count; i++)
            {
                lines.Add(FormatStep(probe.Steps[i]));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatStep(MeasurementSourceRuntimeProbeStep step)
        {
            if (step == null)
            {
                return "- [UNKNOWN] Step data is missing.";
            }

            if (string.IsNullOrWhiteSpace(step.Details))
            {
                return string.Format(CultureInfo.InvariantCulture, "- [{0}] {1}", step.Status ?? "UNKNOWN", step.Name ?? "Step");
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "- [{0}] {1}: {2}",
                step.Status ?? "UNKNOWN",
                step.Name ?? "Step",
                step.Details);
        }

        private sealed class PolicyOption
        {
            public PolicyOption(string name, DesynchronizationPolicyAction action)
            {
                Name = name;
                Action = action;
            }

            public string Name { get; private set; }

            public DesynchronizationPolicyAction Action { get; private set; }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
