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
        private const int MaxSegmentEntries = 100;
        private const int TrendPointCapacity = 90;
        private readonly MeasurementSessionRuntimeFactory _runtimeFactory;
        private readonly string _defaultOutputDir;
        private readonly List<double> _beamEnergyTrend = new List<double>();
        private readonly List<double> _ophirEnergyTrend = new List<double>();
        private readonly List<double> _stabilityTrend = new List<double>();
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
            EventsListBox.Items.Clear();
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
            service.StationarySegmentRecorded += OnStationarySegmentRecorded;
            service.SessionSummaryAvailable += OnSessionSummaryAvailable;
        }

        private void ReplaceService(MeasurementSessionService newService)
        {
            if (_service != null)
            {
                _service.StateChanged -= OnServiceStateChanged;
                _service.LiveMeasurementUpdated -= OnLiveMeasurementUpdated;
                _service.SessionEventRaised -= OnSessionEventRaised;
                _service.StationarySegmentRecorded -= OnStationarySegmentRecorded;
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

        private void OnSessionEventRaised(object sender, SessionEventRaisedEventArgs args)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    delegate
                    {
                        SessionEvent sessionEvent = args != null ? args.SessionEvent : null;
                        UpdateInlineStatusForEvent(sessionEvent);
                        if (sessionEvent != null && sessionEvent.EventType == SessionEventType.StationaryEntered)
                        {
                            _stationaryEntries += 1;
                            StationaryEntriesText.Text = _stationaryEntries.ToString(CultureInfo.InvariantCulture);
                        }

                        AddEvent(FormatEvent(sessionEvent));
                    }));
        }

        private void OnStationarySegmentRecorded(object sender, StationarySegmentRecordedEventArgs args)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    delegate
                    {
                        StationarySegmentResult segment = args != null ? args.Segment : null;
                        if (segment == null)
                        {
                            return;
                        }

                        SegmentsListBox.Items.Insert(0, FormatSegment(segment));
                        while (SegmentsListBox.Items.Count > MaxSegmentEntries)
                        {
                            SegmentsListBox.Items.RemoveAt(SegmentsListBox.Items.Count - 1);
                        }
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
            UpdateSourceModeBadges(firstKey, secondKey);

            var diagnostics = _runtimeFactory.GetDiagnostics(firstKey, secondKey);
            if (diagnostics.Count > 0)
            {
                BindDiagnosticCard(
                    diagnostics[0],
                    BeamDiagnosticTitleText,
                    BeamDiagnosticSummaryText,
                    BeamAcquisitionText,
                    BeamDiagnosticStepsList,
                    BeamDiagnosticDetailsText);
            }

            if (diagnostics.Count > 1)
            {
                BindDiagnosticCard(
                    diagnostics[1],
                    OphirDiagnosticTitleText,
                    OphirDiagnosticSummaryText,
                    OphirAcquisitionText,
                    OphirDiagnosticStepsList,
                    OphirDiagnosticDetailsText);
            }
        }

        private void UpdateState(MeasurementSessionState state)
        {
            StateBadgeText.Text = state.ToString();
            InlineStatusText.Text = BuildStateMessage(state);
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

            ApplyStateBadgeStyle(state);
            UpdateInlineStatusForState(state);
            UpdateWorkflowStyle(state);
        }

        private static string BuildStateMessage(MeasurementSessionState state)
        {
            switch (state)
            {
                case MeasurementSessionState.Initialized:
                    return "Sources are ready. Start acquisition when the stand is prepared.";
                case MeasurementSessionState.Measuring:
                    return "Samples are flowing. Watch pairing, energy, and stability.";
                case MeasurementSessionState.Stationary:
                    return "Stationary mode is active.";
                case MeasurementSessionState.Faulted:
                    return "A critical fault stopped the session.";
                case MeasurementSessionState.Completed:
                    return "Session completed. Review summary and export.";
                default:
                    return "Configure sources and initialize the session.";
            }
        }

        private void UpdateWorkflowStyle(MeasurementSessionState state)
        {
            bool setupActive = state == MeasurementSessionState.Idle || state == MeasurementSessionState.Initialized;
            bool captureActive = state == MeasurementSessionState.Measuring || state == MeasurementSessionState.Stationary;
            bool reviewActive = state == MeasurementSessionState.Completed || state == MeasurementSessionState.Faulted;

            SetWorkflowStep(SetupStepBorder, SetupStepText, setupActive, !setupActive);
            SetWorkflowStep(CaptureStepBorder, CaptureStepText, captureActive, reviewActive);
            SetWorkflowStep(ReviewStepBorder, ReviewStepText, reviewActive, false);
        }

        private void SetWorkflowStep(System.Windows.Controls.Border border, System.Windows.Controls.TextBlock text, bool active, bool complete)
        {
            if (active)
            {
                ApplyStatusVisual(border, text, "StatusInfo");
                return;
            }

            if (complete)
            {
                ApplyStatusVisual(border, text, "StatusSuccess");
                return;
            }

            ApplyStatusVisual(border, text, "StatusNeutral");
        }

        private void UpdateSourceModeBadges(string firstKey, string secondKey)
        {
            SetSourceModeBadge(BeamModeBorder, BeamModeText, firstKey);
            SetSourceModeBadge(OphirModeBorder, OphirModeText, secondKey);
        }

        private void SetSourceModeBadge(System.Windows.Controls.Border border, System.Windows.Controls.TextBlock text, string key)
        {
            string normalizedKey = key ?? string.Empty;
            if (normalizedKey.IndexOf("sim", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ApplyStatusVisual(border, text, "StatusInfo");
                text.Text = "SIMULATION PATH";
                return;
            }

            if (normalizedKey.IndexOf("replay", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ApplyStatusVisual(border, text, "StatusWarning");
                text.Text = "REPLAY CAPTURE";
                return;
            }

            if (normalizedKey.IndexOf("sdk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ApplyStatusVisual(border, text, "StatusSuccess");
                text.Text = "LIVE SDK PATH";
                return;
            }

            ApplyStatusVisual(border, text, "StatusNeutral");
            text.Text = "SOURCE PENDING";
        }

        private void ApplyStatusVisual(System.Windows.Controls.Border border, System.Windows.Controls.TextBlock text, string resourcePrefix)
        {
            border.Background = GetBrush(resourcePrefix + "BackgroundBrush");
            border.BorderBrush = GetBrush(resourcePrefix + "BorderBrush");
            text.Foreground = GetBrush(resourcePrefix + "TextBrush");
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
            StationaryBadgeText.Text = snapshot.IsStationary ? "Stationary" : "Not stationary";
            ApplyStationaryBadgeStyle(snapshot.IsStationary);
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
            StationaryBadgeText.Text = "Not stationary";
            ApplyStationaryBadgeStyle(false);
            OutputStatusText.Text = "Ready";
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

            double step = values.Count == 1 ? 0.0d : width / (values.Count - 1);
            for (int i = 0; i < values.Count; i++)
            {
                double normalized = (values[i] - min) / range;
                double x = values.Count == 1 ? width : i * step;
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
            SummaryText.Text = "No completed session yet";
            SummaryOutcomeText.Text = "Waiting";
            SummaryFinalStateText.Text = "-";
            SummaryFinishedText.Text = "-";
            SummaryPairsText.Text = "0";
            SummaryEventsCountText.Text = "0";
            SummarySegmentsText.Text = "0";
            SummaryDesyncText.Text = "0";
            SummaryFaultsText.Text = "0";
            SummaryTerminationText.Text = "-";
            SummaryTerminationText.ToolTip = null;
            SegmentsListBox.Items.Clear();
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "-";
        }

        private void AddEvent(string message)
        {
            EventsListBox.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message);
            while (EventsListBox.Items.Count > MaxEventEntries)
            {
                EventsListBox.Items.RemoveAt(EventsListBox.Items.Count - 1);
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

        private static string FormatSegment(StationarySegmentResult segment)
        {
            if (segment == null)
            {
                return "Segment data unavailable.";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Segment #{0} | Pair {1} -> {2} | Avg {3}/{4} | {5} ms | {6}",
                segment.SegmentId,
                segment.EntryPairId,
                segment.ExitPairId.HasValue ? segment.ExitPairId.Value.ToString(CultureInfo.InvariantCulture) : "-",
                segment.EntryFirstAverage.ToString("0.0000", CultureInfo.InvariantCulture),
                segment.EntrySecondAverage.ToString("0.0000", CultureInfo.InvariantCulture),
                segment.DurationMs.HasValue ? segment.DurationMs.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-",
                string.IsNullOrWhiteSpace(segment.ExitReason) ? "Closed" : segment.ExitReason);
        }

        private void UpdateSummary(SessionSummary summary)
        {
            if (summary == null)
            {
                ResetSessionReview();
                return;
            }

            SummaryText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} pairs, {2} events, {3} desync, {4} faults",
                summary.FinalState,
                summary.PairCount,
                summary.EventCount,
                summary.DesynchronizationCount,
                summary.FaultCount);
            SummaryOutcomeText.Text = summary.CompletedNormally ? "Completed" : "Aborted";
            SummaryFinalStateText.Text = string.IsNullOrWhiteSpace(summary.FinalState) ? "-" : summary.FinalState;
            SummaryFinishedText.Text = summary.FinishedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            SummaryPairsText.Text = summary.PairCount.ToString(CultureInfo.InvariantCulture);
            SummaryEventsCountText.Text = summary.EventCount.ToString(CultureInfo.InvariantCulture);
            SummarySegmentsText.Text = summary.ClosedStationarySegmentCount.ToString(CultureInfo.InvariantCulture);
            SummaryDesyncText.Text = summary.DesynchronizationCount.ToString(CultureInfo.InvariantCulture);
            SummaryFaultsText.Text = summary.FaultCount.ToString(CultureInfo.InvariantCulture);
            SummaryTerminationText.Text = BuildTerminationText(summary);
            SummaryTerminationText.ToolTip = BuildTerminationTooltip(summary);
            OutputStatusText.Text = summary.CompletedNormally ? "Exported" : "Stopped";
        }

        private void UpdateInlineStatusForState(MeasurementSessionState state)
        {
            switch (state)
            {
                case MeasurementSessionState.Initialized:
                    SetInlineStatus(
                        "Initialized",
                        "Sources are ready. Start the session when the operator is ready to collect pulses.",
                        "StatusInfo");
                    break;
                case MeasurementSessionState.Measuring:
                    SetInlineStatus(
                        "Measuring",
                        "Samples are flowing. Watch the live energy metrics and wait for stationary entry.",
                        "StatusSuccess");
                    break;
                case MeasurementSessionState.Stationary:
                    SetInlineStatus(
                        "Stationary Detected",
                        "A stable segment is active. The session keeps observing for drift or another stable interval.",
                        "StatusWarning");
                    break;
                case MeasurementSessionState.Faulted:
                    SetInlineStatus(
                        "Faulted",
                        "The session stopped because a critical device or pipeline fault was reported.",
                        "StatusDanger");
                    break;
                case MeasurementSessionState.Completed:
                    SetInlineStatus(
                        "Completed",
                        "The current session finished. Review the final metrics and export artifacts before the next run.",
                        "StatusNeutral");
                    break;
                default:
                    SetInlineStatus(
                        "Ready",
                        "Initialize the selected sources to begin a new measurement session.",
                        "StatusInfo");
                    break;
            }
        }

        private void UpdateInlineStatusForEvent(SessionEvent sessionEvent)
        {
            if (sessionEvent == null)
            {
                return;
            }

            switch (sessionEvent.EventType)
            {
                case SessionEventType.StationaryEntered:
                    SetInlineStatus("Stationary Entered", sessionEvent.Message, "StatusWarning");
                    break;
                case SessionEventType.StationaryExited:
                    SetInlineStatus("Stationary Exited", sessionEvent.Message, "StatusWarning");
                    break;
                case SessionEventType.Desynchronized:
                    SetInlineStatus("Desynchronization", sessionEvent.Message, "StatusWarning");
                    break;
                case SessionEventType.Fault:
                    SetInlineStatus("Critical Fault", sessionEvent.Message, "StatusDanger");
                    break;
                case SessionEventType.SessionStarted:
                    SetInlineStatus("Session Started", sessionEvent.Message, "StatusSuccess");
                    break;
                case SessionEventType.SessionStopped:
                    SetInlineStatus("Session Stopped", sessionEvent.Message, "StatusNeutral");
                    break;
            }
        }

        private void SetInlineStatus(string title, string message, string resourcePrefix)
        {
            InlineStatusBorder.Background = GetBrush(resourcePrefix + "BackgroundBrush");
            InlineStatusBorder.BorderBrush = GetBrush(resourcePrefix + "BorderBrush");
            InlineStatusTitleText.Text = title;
            InlineStatusTitleText.Foreground = GetBrush(resourcePrefix + "TextBrush");
            InlineStatusText.Text = message;
            InlineStatusText.Foreground = GetBrush(resourcePrefix + "MessageBrush");
        }

        private void BindDiagnosticCard(
            MeasurementSourceDiagnostic diagnostic,
            System.Windows.Controls.TextBlock titleText,
            System.Windows.Controls.TextBlock summaryText,
            System.Windows.Controls.TextBlock acquisitionText,
            System.Windows.Controls.ListBox stepsList,
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
                    ? "Live acquisition is available in this build."
                    : "Live acquisition is not wired in this build.";

            stepsList.Items.Clear();
            if (probe != null && probe.Steps != null && probe.Steps.Count > 0)
            {
                for (int i = 0; i < probe.Steps.Count; i++)
                {
                    stepsList.Items.Add(FormatStep(probe.Steps[i]));
                }
            }
            else
            {
                stepsList.Items.Add("No detailed probe steps were reported.");
            }

            detailsText.Text = probe != null ? probe.Details : string.Empty;
        }

        private static string FormatStep(MeasurementSourceRuntimeProbeStep step)
        {
            if (step == null)
            {
                return "[UNKNOWN] Step data is missing.";
            }

            if (string.IsNullOrWhiteSpace(step.Details))
            {
                return string.Format(CultureInfo.InvariantCulture, "[{0}] {1}", step.Status ?? "UNKNOWN", step.Name ?? "Step");
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] {1} - {2}",
                step.Status ?? "UNKNOWN",
                step.Name ?? "Step",
                step.Details);
        }

        private void ApplyStateBadgeStyle(MeasurementSessionState state)
        {
            switch (state)
            {
                case MeasurementSessionState.Initialized:
                    ApplyStatusVisual(StateBadgeBorder, StateBadgeText, "StatusInfo");
                    break;
                case MeasurementSessionState.Measuring:
                    ApplyStatusVisual(StateBadgeBorder, StateBadgeText, "StatusSuccess");
                    break;
                case MeasurementSessionState.Stationary:
                    ApplyStatusVisual(StateBadgeBorder, StateBadgeText, "StatusWarning");
                    break;
                case MeasurementSessionState.Faulted:
                    ApplyStatusVisual(StateBadgeBorder, StateBadgeText, "StatusDanger");
                    break;
                case MeasurementSessionState.Completed:
                    ApplyStatusVisual(StateBadgeBorder, StateBadgeText, "StatusNeutral");
                    break;
                default:
                    ApplyStatusVisual(StateBadgeBorder, StateBadgeText, "StatusNeutral");
                    break;
            }
        }

        private void ApplyStationaryBadgeStyle(bool isStationary)
        {
            if (isStationary)
            {
                StationaryBadgeText.Foreground = GetBrush("StatusWarningTextBrush");
                return;
            }

            StationaryBadgeText.Foreground = GetBrush("MutedTextBrush");
        }

        private static string BuildTerminationText(SessionSummary summary)
        {
            if (summary == null)
            {
                return "-";
            }

            if (!string.IsNullOrWhiteSpace(summary.TerminationReasonCode))
            {
                switch (summary.TerminationReasonCode)
                {
                    case "manual-stop":
                        return "Manual Stop";
                    case "service-disposed":
                        return "Service Disposed";
                    case "critical-fault":
                        return "Critical Fault";
                    case "startup-failure":
                        return "Startup Failure";
                    case "desynchronization-threshold-fault":
                        return "Desync Fault";
                    case "desynchronization-threshold-graceful-stop":
                        return "Desync Stop";
                }

                return summary.TerminationReasonCode;
            }

            return summary.CompletedNormally ? "Completed" : "Aborted";
        }

        private static string BuildTerminationTooltip(SessionSummary summary)
        {
            if (summary == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(summary.TerminationReason))
            {
                return summary.TerminationReason;
            }

            return string.IsNullOrWhiteSpace(summary.TerminationReasonCode)
                ? string.Empty
                : summary.TerminationReasonCode;
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
        }
    }
}
