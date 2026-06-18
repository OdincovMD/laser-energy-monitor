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
        private const int MaxEventEntries = 5;
        private static readonly TimeSpan LiveUiUpdateInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan EventLogUpdateInterval = TimeSpan.FromMilliseconds(250);
        private readonly MeasurementSessionRuntimeFactory _runtimeFactory;
        private readonly string _defaultOutputDir;
        private readonly List<string> _eventLines = new List<string>();
        private readonly object _liveUpdateGate = new object();
        private readonly DispatcherTimer _liveUpdateTimer;
        private readonly DispatcherTimer _eventLogTimer;
        private MeasurementSessionService _service;
        private LiveMeasurementSnapshot _pendingLiveSnapshot;
        private string _activeFirstSourceKey;
        private string _activeSecondSourceKey;
        private string _activeStarLabLogPath;
        private int _stationaryEntries;
        private bool _liveUpdatePumpActive;
        private bool _eventLogDirty;
        private bool _isBindingStartupData;

        public MainWindow(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;

            InitializeComponent();
            _liveUpdateTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _liveUpdateTimer.Interval = LiveUiUpdateInterval;
            _liveUpdateTimer.Tick += OnLiveUpdateTimerTick;
            _eventLogTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _eventLogTimer.Interval = EventLogUpdateInterval;
            _eventLogTimer.Tick += OnEventLogTimerTick;
            BindStartupData();
            UpdateState(MeasurementSessionState.Idle);
            ResetLiveValues();
            ResetSessionReview();
            RefreshSourceDiagnostics();
            AddEvent("Application ready.");
        }

        protected override void OnClosed(EventArgs e)
        {
            _liveUpdateTimer.Stop();
            _eventLogTimer.Stop();
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
                BindBeamGagePhysicalDataSources(
                    string.IsNullOrWhiteSpace(_runtimeFactory.ConfiguredBeamGageDataSource)
                        ? new string[0]
                        : new[] { _runtimeFactory.ConfiguredBeamGageDataSource },
                    _runtimeFactory.ConfiguredBeamGageDataSource);

                OutputPathTextBox.Text = Path.Combine(_defaultOutputDir, "measurement-session.xlsx");
                OutputPathTextBox.ToolTip = OutputPathTextBox.Text;
                StarLabLogPathTextBox.Text = _runtimeFactory.ConfiguredStarLabLogPath;
                StarLabLogPathTextBox.ToolTip = StarLabLogPathTextBox.Text;
            }
            finally
            {
                _isBindingStartupData = false;
            }

            UpdateStarLabLogControls();
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

        private void OnUsbDevicesTestClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "USB inventory error",
                delegate
                {
                    string report = _runtimeFactory.RunUsbInventory();
                    DiagnosticsReportTextBox.Text = report;
                    AddEvent("USB inventory completed.");
                    MessageBox.Show(this, report, "USB Devices", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void OnRefreshBeamGageSourcesClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "BeamGage source scan error",
                delegate
                {
                    EnsureBeamGageSdkSelected();
                    DisconnectServiceForBeamGageReconnection();
                    IReadOnlyList<string> dataSources = _runtimeFactory.DiscoverBeamGagePhysicalDataSources();
                    BindBeamGagePhysicalDataSources(dataSources, _runtimeFactory.ConfiguredBeamGageDataSource);
                    AddEvent("BeamGage source scan completed.");
                });
        }

        private void OnReconnectBeamGageClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "BeamGage reconnect error",
                delegate
                {
                    EnsureBeamGageSdkSelected();
                    string selectedDataSource = BeamPhysicalSourceComboBox.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(selectedDataSource))
                    {
                        throw new InvalidOperationException("Select a BeamGage source before connecting.");
                    }

                    DisconnectServiceForBeamGageReconnection();
                    _runtimeFactory.SelectBeamGagePhysicalDataSource(selectedDataSource);
                    EnsureInitializedService(BuildSettings(), true);
                    AddEvent("BeamGage source connected: " + selectedDataSource);
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
                RefreshSourceDiagnostics();
            }
        }

        private void OnClearEventsClicked(object sender, RoutedEventArgs e)
        {
            _eventLines.Clear();
            _eventLogDirty = false;
            if (_eventLogTimer != null)
            {
                _eventLogTimer.Stop();
            }

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
            UpdateBeamGagePhysicalControls();
            UpdateStarLabLogControls();
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
                MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private MeasurementSessionService EnsureInitializedService(SessionSettings settings, bool forceRecreate)
        {
            SynchronizeStarLabLogPath();
            string firstKey = GetSelectedSourceKey(BeamSourceComboBox);
            string secondKey = GetSelectedSourceKey(OphirSourceComboBox);
            string starLabLogPath = StarLabLogPathTextBox.Text;
            bool selectionChanged =
                !string.Equals(firstKey, _activeFirstSourceKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(secondKey, _activeSecondSourceKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(starLabLogPath, _activeStarLabLogPath, StringComparison.OrdinalIgnoreCase);

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
                _activeStarLabLogPath = starLabLogPath;
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
                Dispatcher.BeginInvoke(
                    new Action(EnsureLiveUpdatePumpRunning),
                    DispatcherPriority.Background);
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

        private static string GetSelectedSourceKey(System.Windows.Controls.ComboBox comboBox)
        {
            MeasurementSourceOption option = comboBox != null ? comboBox.SelectedItem as MeasurementSourceOption : null;
            return option != null ? option.Key : string.Empty;
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
        }

        private void EnsureBeamGageSdkSelected()
        {
            if (!IsBeamGageSdkSelected())
            {
                throw new InvalidOperationException("Select BeamGage SDK before scanning or connecting a BeamGage source.");
            }
        }

        private bool IsBeamGageSdkSelected()
        {
            return string.Equals(GetSelectedSourceKey(BeamSourceComboBox), "beam-sdk", StringComparison.OrdinalIgnoreCase);
        }

        private void DisconnectServiceForBeamGageReconnection()
        {
            if (_service != null && IsSessionConfigurationLocked(_service.State))
            {
                throw new InvalidOperationException("Stop the active measurement session before changing the BeamGage source.");
            }

            ReplaceService(null);
            _activeFirstSourceKey = null;
            _activeSecondSourceKey = null;
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
            bool sdkSelected = IsBeamGageSdkSelected();
            BeamPhysicalSourceComboBox.IsEnabled = sdkSelected && !locked;
            RefreshBeamGageSourcesButton.IsEnabled = sdkSelected && !locked;
            ReconnectBeamGageButton.IsEnabled =
                sdkSelected &&
                !locked &&
                BeamPhysicalSourceComboBox.SelectedItem is string;
        }

        private void UpdateStarLabLogControls()
        {
            if (StarLabLogPathTextBox == null || BrowseStarLabLogButton == null)
            {
                return;
            }

            bool locked = _service != null && IsSessionConfigurationLocked(_service.State);
            bool starLabSelected = IsStarLabLogSelected();
            StarLabLogPathTextBox.IsReadOnly = locked || !starLabSelected;
            BrowseStarLabLogButton.IsEnabled = starLabSelected && !locked;
        }

        private bool IsStarLabLogSelected()
        {
            return string.Equals(GetSelectedSourceKey(OphirSourceComboBox), "starlab-log", StringComparison.OrdinalIgnoreCase);
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
        }

        private void RefreshSourceDiagnostics()
        {
            if (BeamSourceComboBox == null || OphirSourceComboBox == null || DiagnosticsReportTextBox == null)
            {
                return;
            }

            SynchronizeStarLabLogPath();
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
            UsbDevicesTestButton.IsEnabled = !locked;
            BeamGageTestButton.IsEnabled = !locked;
            OphirTestButton.IsEnabled = !locked;
            BeamSourceComboBox.IsEnabled = !locked;
            OphirSourceComboBox.IsEnabled = !locked;
            SessionNameTextBox.IsReadOnly = locked;
            WindowSizeTextBox.IsReadOnly = locked;
            EnterThresholdTextBox.IsReadOnly = locked;
            ExitThresholdTextBox.IsReadOnly = locked;
            OutputPathTextBox.IsReadOnly = locked;
            BrowseButton.IsEnabled = !locked;
            UpdateBeamGagePhysicalControls();
            UpdateStarLabLogControls();
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

            if (!snapshot.FirstStabilityMetric.HasValue || !snapshot.SecondStabilityMetric.HasValue)
            {
                return FormatDouble(snapshot.StabilityMetric);
            }

            return FormatDouble(snapshot.StabilityMetric) +
                " B " + FormatDouble(snapshot.FirstStabilityMetric) +
                " O " + FormatDouble(snapshot.SecondStabilityMetric);
        }

        private void AddEvent(string message)
        {
            _eventLines.Insert(0, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message);
            while (_eventLines.Count > MaxEventEntries)
            {
                _eventLines.RemoveAt(_eventLines.Count - 1);
            }

            _eventLogDirty = true;
            if (_eventLogTimer != null && !_eventLogTimer.IsEnabled)
            {
                _eventLogTimer.Start();
            }
        }

        private void OnEventLogTimerTick(object sender, EventArgs e)
        {
            FlushEventLog();
        }

        private void FlushEventLog()
        {
            if (!_eventLogDirty)
            {
                _eventLogTimer.Stop();
                return;
            }

            _eventLogDirty = false;
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

            SummarySamplesText.Text = summary.SampleCount.ToString(CultureInfo.InvariantCulture);
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

    }
}
