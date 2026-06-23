using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using Microsoft.Win32;

namespace LaserEnergyMonitor.Wpf
{
    public partial class MainWindow : Window
    {
        private static readonly TimeSpan LiveUiUpdateInterval = TimeSpan.FromMilliseconds(100);
        private readonly MeasurementSessionRuntimeFactory _runtimeFactory;
        private readonly string _defaultOutputDir;
        private readonly object _liveUpdateGate = new object();
        private readonly DispatcherTimer _liveUpdateTimer;
        private MeasurementSessionService _service;
        private LiveMeasurementSnapshot _pendingLiveSnapshot;
        private string _activeBeamDataSource;
        private string _activeStarLabLogPath;
        private int _stationaryEntries;
        private bool _liveUpdatePumpActive;
        private bool _isBindingStartupData;

        public MainWindow(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;

            InitializeComponent();
            _liveUpdateTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _liveUpdateTimer.Interval = LiveUiUpdateInterval;
            _liveUpdateTimer.Tick += OnLiveUpdateTimerTick;

            BindStartupData();
            UpdateState(MeasurementSessionState.Idle);
            ResetLiveValues();
            ResetSessionReview();
            AddStatus("Application ready.");
        }

        protected override void OnClosed(EventArgs e)
        {
            _liveUpdateTimer.Stop();
            ReplaceService(null);
            base.OnClosed(e);
        }

        private void BindStartupData()
        {
            _isBindingStartupData = true;
            try
            {
                BindBeamGagePhysicalDataSources(
                    string.IsNullOrWhiteSpace(_runtimeFactory.ConfiguredBeamGageDataSource)
                        ? new string[0]
                        : new[] { _runtimeFactory.ConfiguredBeamGageDataSource },
                    _runtimeFactory.ConfiguredBeamGageDataSource);

                OutputPathTextBox.Text = Path.Combine(_defaultOutputDir, "measurement-session.xlsx");
                OutputPathTextBox.ToolTip = OutputPathTextBox.Text;
                StarLabLogPathTextBox.Text = _runtimeFactory.ConfiguredStarLabLogPath;
                StarLabLogPathTextBox.ToolTip = StarLabLogPathTextBox.Text;
                UpdateOphirStatus();
            }
            finally
            {
                _isBindingStartupData = false;
            }
        }

        private void OnStartClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Start error",
                delegate
                {
                    MeasurementSessionService service = EnsureInitializedService(BuildSettings(), false);
                    service.Start();
                    AddStatus("Session started.");
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
                    AddStatus("Session stopped.");
                    ResetLiveValues();
                });
        }

        private void OnRefreshBeamGageSourcesClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "BeamGage source scan error",
                delegate
                {
                    DisconnectServiceForBeamGageReconnection();
                    IReadOnlyList<string> dataSources = _runtimeFactory.DiscoverBeamGagePhysicalDataSources();
                    BindBeamGagePhysicalDataSources(dataSources, _runtimeFactory.ConfiguredBeamGageDataSource);
                    AddStatus("BeamGage source scan completed.");
                });
        }

        private void OnReconnectBeamGageClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "BeamGage reconnect error",
                delegate
                {
                    string selectedDataSource = BeamPhysicalSourceComboBox.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(selectedDataSource))
                    {
                        throw new InvalidOperationException("Select a BeamGage source before connecting.");
                    }

                    DisconnectServiceForBeamGageReconnection();
                    _runtimeFactory.SelectBeamGagePhysicalDataSource(selectedDataSource);
                    EnsureInitializedService(BuildSettings(), true);
                    AddStatus("BeamGage source connected: " + selectedDataSource);
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

        private void OnBrowseStarLabLogClicked(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "StarLab log (*.txt)|*.txt|All files (*.*)|*.*";
            dialog.Title = "Select StarLab log file";
            dialog.FileName = Path.GetFileName(StarLabLogPathTextBox.Text);
            string directory = Path.GetDirectoryName(StarLabLogPathTextBox.Text);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog(this) == true)
            {
                StarLabLogPathTextBox.Text = dialog.FileName;
                StarLabLogPathTextBox.ToolTip = dialog.FileName;
                SynchronizeStarLabLogPath();
                UpdateOphirStatus();
                DisconnectServiceForSourceChange();
                AddStatus("StarLab log selected: " + dialog.FileName);
            }
        }

        private void OnBeamGagePhysicalSourceSelectionChanged(object sender, EventArgs e)
        {
            if (_isBindingStartupData)
            {
                return;
            }

            UpdateBeamGagePhysicalControls();
        }

        private void RunUiAction(string title, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AddStatus(ex.Message, true);
                MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private MeasurementSessionService EnsureInitializedService(SessionSettings settings, bool forceRecreate)
        {
            SynchronizeStarLabLogPath();
            string beamDataSource = _runtimeFactory.ConfiguredBeamGageDataSource;
            string starLabLogPath = StarLabLogPathTextBox.Text;

            if (string.IsNullOrWhiteSpace(beamDataSource))
            {
                throw new InvalidOperationException("Scan and connect a BeamGage source before starting.");
            }

            bool selectionChanged =
                !string.Equals(beamDataSource, _activeBeamDataSource, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(starLabLogPath, _activeStarLabLogPath, StringComparison.OrdinalIgnoreCase);

            if (_service != null && IsSessionConfigurationLocked(_service.State))
            {
                throw new InvalidOperationException("Stop the active measurement session before reconfiguring sources or session settings.");
            }

            if (!forceRecreate && !selectionChanged && _service != null && _service.State == MeasurementSessionState.Initialized)
            {
                return _service;
            }

            MeasurementSessionService newService = _runtimeFactory.Create();
            try
            {
                AttachServiceEvents(newService);
                newService.Initialize(settings);
                ReplaceService(newService);
                ResetSessionReview();
                _activeBeamDataSource = beamDataSource;
                _activeStarLabLogPath = starLabLogPath;
                UpdateState(_service.State);
                UpdateBeamStatus("Connected: " + beamDataSource, false);
                UpdateOphirStatus();
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
            if (args == null || args.Snapshot == null)
            {
                return;
            }

            bool startPump = false;
            lock (_liveUpdateGate)
            {
                _pendingLiveSnapshot = args.Snapshot;
                if (!_liveUpdatePumpActive)
                {
                    _liveUpdatePumpActive = true;
                    startPump = true;
                }
            }

            if (startPump)
            {
                Dispatcher.BeginInvoke(new Action(EnsureLiveUpdatePumpRunning), DispatcherPriority.Background);
            }
        }

        private void EnsureLiveUpdatePumpRunning()
        {
            if (DrainPendingLiveUpdate() && !_liveUpdateTimer.IsEnabled)
            {
                _liveUpdateTimer.Start();
            }
        }

        private void OnLiveUpdateTimerTick(object sender, EventArgs e)
        {
            DrainPendingLiveUpdate();
        }

        private bool DrainPendingLiveUpdate()
        {
            LiveMeasurementSnapshot snapshot;
            lock (_liveUpdateGate)
            {
                snapshot = _pendingLiveSnapshot;
                _pendingLiveSnapshot = null;
                if (snapshot == null)
                {
                    _liveUpdatePumpActive = false;
                    _liveUpdateTimer.Stop();
                    return false;
                }
            }

            UpdateLiveValues(snapshot);
            return true;
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

                        AddStatus(FormatEvent(sessionEvent));
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
                RollingWindowSize = ParseInt(WindowSizeTextBox.Text, OperatorSessionSettingsPolicy.DefaultRollingWindowSize),
                EnterThresholdPercent = ParseDouble(EnterThresholdTextBox.Text, OperatorSessionSettingsPolicy.DefaultEnterThresholdPercent),
                ExitThresholdPercent = ParseDouble(ExitThresholdTextBox.Text, OperatorSessionSettingsPolicy.DefaultExitThresholdPercent),
                OutputPath = OutputPathTextBox.Text
            };
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

        private void BindBeamGagePhysicalDataSources(IReadOnlyList<string> dataSources, string preferredDataSource)
        {
            BeamPhysicalSourceComboBox.ItemsSource = dataSources ?? new string[0];
            BeamPhysicalSourceComboBox.SelectedIndex = -1;

            for (int i = 0; i < BeamPhysicalSourceComboBox.Items.Count; i++)
            {
                string candidate = BeamPhysicalSourceComboBox.Items[i] as string;
                if (string.Equals(candidate, preferredDataSource, StringComparison.OrdinalIgnoreCase))
                {
                    BeamPhysicalSourceComboBox.SelectedIndex = i;
                    break;
                }
            }

            if (BeamPhysicalSourceComboBox.SelectedIndex < 0 && BeamPhysicalSourceComboBox.Items.Count > 0)
            {
                BeamPhysicalSourceComboBox.SelectedIndex = 0;
            }

            UpdateBeamGagePhysicalControls();
            UpdateBeamStatus();
        }

        private void DisconnectServiceForBeamGageReconnection()
        {
            if (_service != null && IsSessionConfigurationLocked(_service.State))
            {
                throw new InvalidOperationException("Stop the active measurement session before changing the BeamGage source.");
            }

            ReplaceService(null);
            _activeBeamDataSource = null;
            _activeStarLabLogPath = null;
            UpdateState(MeasurementSessionState.Idle);
        }

        private void DisconnectServiceForSourceChange()
        {
            if (_service != null && IsSessionConfigurationLocked(_service.State))
            {
                throw new InvalidOperationException("Stop the active measurement session before changing sources.");
            }

            ReplaceService(null);
            _activeBeamDataSource = null;
            _activeStarLabLogPath = null;
            UpdateState(MeasurementSessionState.Idle);
        }

        private void UpdateBeamGagePhysicalControls()
        {
            if (BeamPhysicalSourceComboBox == null ||
                RefreshBeamGageSourcesButton == null ||
                ReconnectBeamGageButton == null)
            {
                return;
            }

            bool locked = _service != null && IsSessionConfigurationLocked(_service.State);
            BeamPhysicalSourceComboBox.IsEnabled = !locked;
            RefreshBeamGageSourcesButton.IsEnabled = !locked;
            ReconnectBeamGageButton.IsEnabled = !locked && BeamPhysicalSourceComboBox.SelectedItem is string;
        }

        private void SynchronizeStarLabLogPath()
        {
            if (StarLabLogPathTextBox == null)
            {
                return;
            }

            string path = StarLabLogPathTextBox.Text ?? string.Empty;
            StarLabLogPathTextBox.ToolTip = path;
            _runtimeFactory.SelectStarLabLogFile(path);
            UpdateOphirStatus();
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
            SessionNameTextBox.IsReadOnly = locked;
            WindowSizeTextBox.IsReadOnly = locked;
            EnterThresholdTextBox.IsReadOnly = locked;
            ExitThresholdTextBox.IsReadOnly = locked;
            OutputPathTextBox.IsReadOnly = locked;
            StarLabLogPathTextBox.IsReadOnly = locked;
            BrowseButton.IsEnabled = !locked;
            BrowseStarLabLogButton.IsEnabled = !locked;
            UpdateBeamGagePhysicalControls();
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
            RecordIdText.Text = snapshot.RecordId.ToString(CultureInfo.InvariantCulture);
            LastUpdateText.Text = snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            BeamEnergyText.Text = FormatEnergy(snapshot.FirstEnergy);
            OphirEnergyText.Text = FormatEnergy(snapshot.SecondEnergy);
            BeamAverageText.Text = FormatEnergy(snapshot.FirstAverage);
            OphirAverageText.Text = FormatEnergy(snapshot.SecondAverage);
            StabilityText.Text = FormatStability(snapshot);
            UpdateStabilityIndicator(BeamStabilityTile, BeamStabilityText, snapshot.FirstStabilityMetric);
            UpdateStabilityIndicator(OphirStabilityTile, OphirStabilityText, snapshot.SecondStabilityMetric);
            UpdateStabilityIndicator(OverallStabilityTile, StabilityText, snapshot.StabilityMetric);
        }

        private void ResetLiveValues()
        {
            lock (_liveUpdateGate)
            {
                _pendingLiveSnapshot = null;
                _liveUpdatePumpActive = false;
            }

            if (_liveUpdateTimer != null)
            {
                _liveUpdateTimer.Stop();
            }

            RecordIdText.Text = "-";
            LastUpdateText.Text = "-";
            BeamEnergyText.Text = "-";
            OphirEnergyText.Text = "-";
            BeamAverageText.Text = "-";
            OphirAverageText.Text = "-";
            StabilityText.Text = "-";
            ResetStabilityIndicator(BeamStabilityTile, BeamStabilityText);
            ResetStabilityIndicator(OphirStabilityTile, OphirStabilityText);
            ResetStabilityIndicator(OverallStabilityTile, StabilityText);
            _stationaryEntries = 0;
            StationaryEntriesText.Text = "0";
        }

        private void ResetSessionReview()
        {
            SummarySamplesText.Text = "0";
            SummaryEventsCountText.Text = "0";
            SummarySegmentsText.Text = "0";
            SummaryFaultsText.Text = "0";
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "-";
        }

        private static string FormatEnergy(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.000000", CultureInfo.InvariantCulture) : "-";
        }

        private static string FormatStability(LiveMeasurementSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.StabilityMetric.HasValue)
            {
                return "-";
            }

            return FormatPercent(snapshot.StabilityMetric);
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) + "%" : "-";
        }

        private void UpdateStabilityIndicator(System.Windows.Controls.Border tile, System.Windows.Controls.TextBlock target, double? metric)
        {
            if (target == null)
            {
                return;
            }

            if (!metric.HasValue)
            {
                target.Text = "Warming up";
                target.Foreground = GetBrush("StatusNeutralTextBrush");
                ApplyStabilityBrushes(tile, "StatusNeutralBackgroundBrush", "StatusNeutralBorderBrush");
                return;
            }

            double enterThreshold = ParseDouble(EnterThresholdTextBox.Text, OperatorSessionSettingsPolicy.DefaultEnterThresholdPercent);
            double exitThreshold = ParseDouble(ExitThresholdTextBox.Text, OperatorSessionSettingsPolicy.DefaultExitThresholdPercent);
            if (exitThreshold < enterThreshold)
            {
                exitThreshold = enterThreshold;
            }

            string label;
            string brushKey;
            string backgroundBrushKey;
            string borderBrushKey;
            if (metric.Value <= enterThreshold)
            {
                label = "Stable";
                brushKey = "StatusSuccessTextBrush";
                backgroundBrushKey = "StatusSuccessBackgroundBrush";
                borderBrushKey = "StatusSuccessBorderBrush";
            }
            else if (metric.Value <= exitThreshold)
            {
                label = "Near";
                brushKey = "StatusWarningTextBrush";
                backgroundBrushKey = "StatusWarningBackgroundBrush";
                borderBrushKey = "StatusWarningBorderBrush";
            }
            else
            {
                label = "Unstable";
                brushKey = "StatusDangerTextBrush";
                backgroundBrushKey = "StatusDangerBackgroundBrush";
                borderBrushKey = "StatusDangerBorderBrush";
            }

            target.Text = FormatPercent(metric) + " " + label;
            target.Foreground = GetBrush(brushKey);
            ApplyStabilityBrushes(tile, backgroundBrushKey, borderBrushKey);
        }

        private void ResetStabilityIndicator(System.Windows.Controls.Border tile, System.Windows.Controls.TextBlock target)
        {
            if (target != null)
            {
                target.Text = "-";
                target.Foreground = GetBrush("TextBrush");
            }

            ApplyStabilityBrushes(tile, "PanelAltBrush", "LineBrush");
        }

        private void ApplyStabilityBrushes(System.Windows.Controls.Border tile, string backgroundBrushKey, string borderBrushKey)
        {
            if (tile == null)
            {
                return;
            }

            tile.Background = GetBrush(backgroundBrushKey);
            tile.BorderBrush = GetBrush(borderBrushKey);
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

            SummarySamplesText.Text = summary.SampleCount.ToString(CultureInfo.InvariantCulture);
            SummaryEventsCountText.Text = summary.EventCount.ToString(CultureInfo.InvariantCulture);
            SummarySegmentsText.Text = summary.ClosedStationarySegmentCount.ToString(CultureInfo.InvariantCulture);
            SummaryFaultsText.Text = summary.FaultCount.ToString(CultureInfo.InvariantCulture);
        }

        private void UpdateBeamStatus()
        {
            string selected = BeamPhysicalSourceComboBox != null ? BeamPhysicalSourceComboBox.SelectedItem as string : null;
            string configured = _runtimeFactory.ConfiguredBeamGageDataSource;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                UpdateBeamStatus("Connected: " + configured, false);
            }
            else if (!string.IsNullOrWhiteSpace(selected))
            {
                UpdateBeamStatus("Selected: " + selected, false);
            }
            else
            {
                UpdateBeamStatus("Scan BeamGage sources.", true);
            }
        }

        private void UpdateBeamStatus(string message, bool warning)
        {
            BeamStatusText.Text = message;
            BeamStatusText.Foreground = warning ? GetBrush("StatusWarningTextBrush") : GetBrush("StatusSuccessTextBrush");
        }

        private void UpdateOphirStatus()
        {
            string path = StarLabLogPathTextBox != null ? StarLabLogPathTextBox.Text : string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                OphirStatusText.Text = "Select StarLab Data_log.txt.";
                OphirStatusText.Foreground = GetBrush("StatusWarningTextBrush");
                return;
            }

            bool exists = File.Exists(path);
            OphirStatusText.Text = exists ? "Ready: " + path : "Waiting for file: " + path;
            OphirStatusText.Foreground = exists ? GetBrush("StatusSuccessTextBrush") : GetBrush("StatusWarningTextBrush");
        }

        private void AddStatus(string message)
        {
            AddStatus(message, false);
        }

        private void AddStatus(string message, bool error)
        {
            LastStatusText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
            LastStatusText.Foreground = error ? GetBrush("StatusDangerTextBrush") : GetBrush("StatusNeutralTextBrush");
        }
    }
}
