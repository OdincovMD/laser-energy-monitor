using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;
using LaserEnergyMonitor.Infrastructure.Excel;
using LaserEnergyMonitor.Infrastructure.Ophir;
using Microsoft.Win32;
using WpfLine = System.Windows.Shapes.Line;
using WpfPolyline = System.Windows.Shapes.Polyline;

namespace LaserEnergyMonitor.Wpf
{
    public partial class MainWindow : Window
    {
        private static readonly TimeSpan LiveUiUpdateInterval = TimeSpan.FromMilliseconds(100);
        private readonly MeasurementSessionRuntimeFactory _runtimeFactory;
        private readonly string _defaultOutputDir;
        private readonly object _liveUpdateGate = new object();
        private readonly DispatcherTimer _liveUpdateTimer;
        private readonly MeasurementAnalyticsWorkbookReader _analyticsReader;
        private readonly MeasurementAnalyticsAnalyzer _analyticsAnalyzer;
        private readonly MeasurementAnalyticsWorkbookExporter _analyticsExporter;
        private readonly OperatorUserSettingsStore _operatorSettingsStore;
        private readonly SessionPreflightService _preflightService;
        private MeasurementSessionService _service;
        private LiveMeasurementSnapshot _pendingLiveSnapshot;
        private AnalyticsReport _analyticsReport;
        private string _activeBeamDataSource;
        private string _activeStarLabLogPath;
        private int _stationaryEntries;
        private bool _liveUpdatePumpActive;
        private bool _isBindingStartupData;

        public MainWindow(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir, OperatorUserSettingsStore operatorSettingsStore)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;
            _operatorSettingsStore = operatorSettingsStore ?? throw new ArgumentNullException("operatorSettingsStore");

            InitializeComponent();
            _liveUpdateTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _liveUpdateTimer.Interval = LiveUiUpdateInterval;
            _liveUpdateTimer.Tick += OnLiveUpdateTimerTick;
            _analyticsReader = new MeasurementAnalyticsWorkbookReader();
            _analyticsAnalyzer = new MeasurementAnalyticsAnalyzer();
            _analyticsExporter = new MeasurementAnalyticsWorkbookExporter();
            _preflightService = new SessionPreflightService(new StarLabLogPreflightProbe(), _runtimeFactory);
            EnergyChartCanvas.SizeChanged += OnAnalyticsChartSizeChanged;
            StabilityChartCanvas.SizeChanged += OnAnalyticsChartSizeChanged;

            BindStartupData();
            ResetAnalyticsView();
            UpdateState(MeasurementSessionState.Idle);
            ResetLiveValues();
            ResetSessionReview();
            AddStatus("Application ready.");
        }

        private void OnOpenAnalyticsWorkbookClicked(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "Measurement workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*";
            dialog.Title = "Open measurement workbook";
            if (dialog.ShowDialog(this) == true)
            {
                LoadAnalyticsWorkbook(dialog.FileName);
            }
        }

        private void OnExportAnalyticsClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Analytics export error",
                delegate
                {
                    if (_analyticsReport == null)
                    {
                        throw new InvalidOperationException("Open a measurement workbook before exporting analytics.");
                    }

                    SaveFileDialog dialog = new SaveFileDialog();
                    dialog.Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*";
                    dialog.Title = "Export analytics workbook";
                    dialog.FileName = BuildDefaultAnalyticsFileName(_analyticsReport.SourcePath);
                    string directory = Path.GetDirectoryName(_analyticsReport.SourcePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        dialog.InitialDirectory = directory;
                    }

                    if (dialog.ShowDialog(this) == true)
                    {
                        _analyticsExporter.Export(_analyticsReport, dialog.FileName);
                        AnalyticsStatusText.Text = "Analytics exported: " + dialog.FileName;
                        AnalyticsStatusText.Foreground = GetBrush("StatusSuccessTextBrush");
                    }
                });
        }

        private void LoadAnalyticsWorkbook(string path)
        {
            RunUiAction(
                "Analytics load error",
                delegate
                {
                    AnalyticsStatusText.Text = "Loading analytics...";
                    AnalyticsStatusText.Foreground = GetBrush("StatusNeutralTextBrush");
                    MeasurementAnalyticsWorkbook workbook = _analyticsReader.Read(path);
                    _analyticsReport = _analyticsAnalyzer.Analyze(workbook);
                    BindAnalyticsReport(_analyticsReport);
                });
        }

        private void ResetAnalyticsView()
        {
            _analyticsReport = null;
            AnalyticsPathTextBox.Text = "No workbook selected.";
            AnalyticsDurationText.Text = "-";
            AnalyticsSamplesText.Text = "-";
            AnalyticsFaultsText.Text = "-";
            AnalyticsStationaryText.Text = "-";
            AnalyticsWarningsText.Text = "No analytics workbook loaded.";
            SourceStatsGrid.ItemsSource = null;
            StationaryStatsGrid.ItemsSource = null;
            ExportAnalyticsButton.IsEnabled = false;
            EnergyChartCanvas.Children.Clear();
            StabilityChartCanvas.Children.Clear();
        }

        private void BindAnalyticsReport(AnalyticsReport report)
        {
            AnalyticsPathTextBox.Text = report.SourcePath ?? string.Empty;
            AnalyticsPathTextBox.ToolTip = AnalyticsPathTextBox.Text;
            AnalyticsDurationText.Text = FormatDuration(report.DurationSeconds);
            AnalyticsSamplesText.Text = report.SampleCount.ToString(CultureInfo.InvariantCulture);
            AnalyticsFaultsText.Text = report.FaultCount.ToString(CultureInfo.InvariantCulture);
            AnalyticsStationaryText.Text = FormatPercent(report.Stationary != null ? report.Stationary.BothStationaryPercent : null);
            AnalyticsStatusText.Text = "Analytics loaded.";
            AnalyticsStatusText.Foreground = GetBrush("StatusSuccessTextBrush");
            AnalyticsWarningsText.Text = report.Warnings.Count > 0
                ? string.Join(Environment.NewLine, report.Warnings)
                : "No warnings.";
            SourceStatsGrid.ItemsSource = BuildSourceStatsRows(report);
            StationaryStatsGrid.ItemsSource = BuildStationaryRows(report);
            ExportAnalyticsButton.IsEnabled = true;
            RedrawAnalyticsCharts();
        }

        private void OnAnalyticsChartSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawAnalyticsCharts();
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveOperatorSettings();
            _liveUpdateTimer.Stop();
            ReplaceService(null);
            base.OnClosed(e);
        }

        private void BindStartupData()
        {
            _isBindingStartupData = true;
            try
            {
                OperatorUserSettings saved = LoadOperatorSettings();
                string beamDataSource = FirstNonEmpty(saved.BeamGageDataSource, _runtimeFactory.ConfiguredBeamGageDataSource);
                if (!string.IsNullOrWhiteSpace(beamDataSource))
                {
                    _runtimeFactory.SelectBeamGagePhysicalDataSource(beamDataSource);
                }

                BindBeamGagePhysicalDataSources(
                    string.IsNullOrWhiteSpace(beamDataSource)
                        ? new string[0]
                        : new[] { beamDataSource },
                    beamDataSource);

                SessionNameTextBox.Text = FirstNonEmpty(saved.SessionName, SessionNameTextBox.Text);
                WindowSizeTextBox.Text = FirstNonEmpty(saved.RollingWindowSize, WindowSizeTextBox.Text);
                EnterThresholdTextBox.Text = FirstNonEmpty(saved.EnterThresholdPercent, EnterThresholdTextBox.Text);
                ExitThresholdTextBox.Text = FirstNonEmpty(saved.ExitThresholdPercent, ExitThresholdTextBox.Text);
                OutputPathTextBox.Text = FirstNonEmpty(saved.OutputPath, Path.Combine(_defaultOutputDir, "measurement-session.xlsx"));
                OutputPathTextBox.ToolTip = OutputPathTextBox.Text;
                StarLabLogPathTextBox.Text = FirstNonEmpty(saved.StarLabLogPath, _runtimeFactory.ConfiguredStarLabLogPath);
                StarLabLogPathTextBox.ToolTip = StarLabLogPathTextBox.Text;
                SynchronizeStarLabLogPath();
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
                    SessionPreflightReport report = RunSelfTest();
                    if (report.HasFailures)
                    {
                        throw new InvalidOperationException(_preflightService.BuildFailureMessage(report));
                    }

                    SaveOperatorSettings();
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
                    SaveOperatorSettings();
                    AddStatus("BeamGage source connected: " + selectedDataSource);
                });
        }

        private void OnSelfTestClicked(object sender, RoutedEventArgs e)
        {
            RunUiAction(
                "Self-Test error",
                delegate
                {
                    SessionPreflightReport report = RunSelfTest();
                    SaveOperatorSettings();
                    if (report.HasFailures)
                    {
                        AddStatus("Self-Test failed. Review failed checks.", true);
                    }
                    else if (report.HasWarnings)
                    {
                        AddStatus("Self-Test completed with warnings.");
                    }
                    else
                    {
                        AddStatus("Self-Test passed.");
                    }
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
                SaveOperatorSettings();
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
                SaveOperatorSettings();
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
            SaveOperatorSettings();
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

        private SessionPreflightReport RunSelfTest()
        {
            SynchronizeStarLabLogPath();
            string selectedBeamDataSource = BeamPhysicalSourceComboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selectedBeamDataSource))
            {
                _runtimeFactory.SelectBeamGagePhysicalDataSource(selectedBeamDataSource);
            }
            else
            {
                selectedBeamDataSource = _runtimeFactory.ConfiguredBeamGageDataSource;
            }

            SessionPreflightReport report = _preflightService.Run(
                new SessionPreflightRequest
                {
                    Settings = BuildSettings(),
                    StarLabLogPath = StarLabLogPathTextBox.Text,
                    StarLabEnergyColumnName = _runtimeFactory.ConfiguredStarLabEnergyColumnName,
                    BeamGageSelectedSource = selectedBeamDataSource,
                    BuildConfiguration = ResolveBuildConfiguration()
                });

            BindPreflightResults(report);
            _runtimeFactory.LogPreflightReport(report);
            return report;
        }

        private void BindPreflightResults(SessionPreflightReport report)
        {
            if (PreflightResultsListBox == null)
            {
                return;
            }

            PreflightResultsListBox.ItemsSource = report != null
                ? report.Checks.Select(FormatPreflightCheck).ToArray()
                : new string[0];
        }

        private static string FormatPreflightCheck(PreflightCheckResult check)
        {
            if (check == null)
            {
                return string.Empty;
            }

            return check.Status.ToString().ToUpperInvariant() + " - " + check.Name + ": " + check.Message;
        }

        private OperatorUserSettings LoadOperatorSettings()
        {
            try
            {
                return _operatorSettingsStore.Load();
            }
            catch
            {
                return new OperatorUserSettings();
            }
        }

        private void SaveOperatorSettings()
        {
            if (_isBindingStartupData ||
                _operatorSettingsStore == null ||
                SessionNameTextBox == null ||
                WindowSizeTextBox == null ||
                EnterThresholdTextBox == null ||
                ExitThresholdTextBox == null ||
                OutputPathTextBox == null ||
                StarLabLogPathTextBox == null ||
                BeamPhysicalSourceComboBox == null)
            {
                return;
            }

            try
            {
                _operatorSettingsStore.Save(
                    new OperatorUserSettings
                    {
                        SessionName = SessionNameTextBox.Text,
                        RollingWindowSize = WindowSizeTextBox.Text,
                        EnterThresholdPercent = EnterThresholdTextBox.Text,
                        ExitThresholdPercent = ExitThresholdTextBox.Text,
                        OutputPath = OutputPathTextBox.Text,
                        StarLabLogPath = StarLabLogPathTextBox.Text,
                        BeamGageDataSource = FirstNonEmpty(
                            BeamPhysicalSourceComboBox.SelectedItem as string,
                            _runtimeFactory.ConfiguredBeamGageDataSource)
                    });
            }
            catch
            {
            }
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }

        private static string ResolveBuildConfiguration()
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
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
            SelfTestButton.IsEnabled = !locked;
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

            return FormatStabilityScoreFromInstability(snapshot.StabilityMetric);
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) + "%" : "-";
        }

        private static string FormatStabilityScoreFromInstability(double? instabilityPercent)
        {
            return instabilityPercent.HasValue
                ? ToStabilityScore(instabilityPercent.Value).ToString("0.###", CultureInfo.InvariantCulture) + "%"
                : "-";
        }

        private static double? ToStabilityScore(double? instabilityPercent)
        {
            return instabilityPercent.HasValue ? ToStabilityScore(instabilityPercent.Value) : (double?)null;
        }

        private static double ToStabilityScore(double instabilityPercent)
        {
            if (double.IsNaN(instabilityPercent) || double.IsInfinity(instabilityPercent))
            {
                return 0.0d;
            }

            return Math.Max(0.0d, Math.Min(100.0d, 100.0d - instabilityPercent));
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

            target.Text = FormatStabilityScoreFromInstability(metric) + " " + label;
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

        private List<SourceStatsRow> BuildSourceStatsRows(AnalyticsReport report)
        {
            List<SourceStatsRow> rows = new List<SourceStatsRow>();
            AddSourceStatsRow(rows, report.FirstSource);
            AddSourceStatsRow(rows, report.SecondSource);
            return rows;
        }

        private static void AddSourceStatsRow(List<SourceStatsRow> rows, SourceStatistics source)
        {
            if (source == null)
            {
                return;
            }

            rows.Add(
                new SourceStatsRow
                {
                    Source = string.IsNullOrWhiteSpace(source.SourceId) ? source.Slot : source.SourceId,
                    Mean = FormatEnergyValue(source.Mean),
                    StandardDeviation = FormatEnergyValue(source.StandardDeviation),
                    CoefficientOfVariation = FormatPercent(source.CoefficientOfVariationPercent),
                    Drift = FormatPercent(source.DriftPercent),
                    Stationary = FormatPercent(source.StationaryPercent)
                });
        }

        private List<MetricRow> BuildStationaryRows(AnalyticsReport report)
        {
            StationaryStatistics stationary = report.Stationary ?? new StationaryStatistics();
            ComparisonStatistics comparison = report.Comparison ?? new ComparisonStatistics();
            return new List<MetricRow>
            {
                new MetricRow { Metric = "Stationary segments", Value = stationary.SegmentCount.ToString(CultureInfo.InvariantCulture) },
                new MetricRow { Metric = "Total stationary time", Value = FormatDuration(stationary.TotalDurationSeconds) },
                new MetricRow { Metric = "Longest segment", Value = FormatDuration(stationary.LongestDurationSeconds) },
                new MetricRow { Metric = "Average segment", Value = FormatDuration(stationary.AverageDurationSeconds) },
                new MetricRow { Metric = "Entry score avg", Value = FormatStabilityScoreFromInstability(stationary.AverageEntryStabilityPercent) },
                new MetricRow { Metric = "Exit score avg", Value = FormatStabilityScoreFromInstability(stationary.AverageExitStabilityPercent) },
                new MetricRow { Metric = "Mean Beam/Ophir ratio", Value = FormatNumber(comparison.MeanRatio) },
                new MetricRow { Metric = "Correlation", Value = FormatNumber(comparison.Correlation) },
                new MetricRow { Metric = "Average abs delta", Value = FormatEnergyValue(comparison.AverageAbsoluteDelta) }
            };
        }

        private void RedrawAnalyticsCharts()
        {
            if (_analyticsReport == null)
            {
                return;
            }

            DrawChart(
                EnergyChartCanvas,
                new[]
                {
                    new ChartSeries("BeamGage", _analyticsReport.ChartPoints.Select(point => point.FirstEnergy).ToList(), GetBrush("AccentBrush")),
                    new ChartSeries("Ophir", _analyticsReport.ChartPoints.Select(point => point.SecondEnergy).ToList(), GetBrush("SuccessBrush"))
                });
            DrawChart(
                StabilityChartCanvas,
                new[]
                {
                    new ChartSeries("BeamGage", _analyticsReport.ChartPoints.Select(point => ToStabilityScore(point.FirstStabilityPercent)).ToList(), GetBrush("AccentBrush")),
                    new ChartSeries("Ophir", _analyticsReport.ChartPoints.Select(point => ToStabilityScore(point.SecondStabilityPercent)).ToList(), GetBrush("SuccessBrush")),
                    new ChartSeries("Overall", _analyticsReport.ChartPoints.Select(point => ToStabilityScore(point.OverallStabilityPercent)).ToList(), GetBrush("WarningBrush"))
                });
        }

        private void DrawChart(Canvas canvas, IEnumerable<ChartSeries> series)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.Children.Clear();
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width < 20.0d || height < 20.0d)
            {
                return;
            }

            List<ChartSeries> seriesList = series.ToList();
            List<double> values = seriesList
                .SelectMany(item => item.Values)
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToList();
            if (values.Count == 0)
            {
                AddChartMessage(canvas, "No chart data.");
                return;
            }

            double min = values.Min();
            double max = values.Max();
            if (Math.Abs(max - min) < 0.000000001d)
            {
                min -= 1.0d;
                max += 1.0d;
            }

            DrawChartGrid(canvas, width, height);
            int maxCount = seriesList.Max(item => item.Values.Count);
            foreach (ChartSeries item in seriesList)
            {
                WpfPolyline line = new WpfPolyline
                {
                    Stroke = item.Brush,
                    StrokeThickness = 2.0d,
                    SnapsToDevicePixels = true
                };

                for (int i = 0; i < item.Values.Count; i++)
                {
                    double? value = item.Values[i];
                    if (!value.HasValue)
                    {
                        continue;
                    }

                    double x = maxCount <= 1 ? width / 2.0d : i * (width - 12.0d) / (maxCount - 1) + 6.0d;
                    double normalized = (value.Value - min) / (max - min);
                    double y = height - 8.0d - normalized * (height - 18.0d);
                    line.Points.Add(new Point(x, y));
                }

                if (line.Points.Count > 1)
                {
                    canvas.Children.Add(line);
                }
            }

            AddChartScaleLabel(canvas, min, max);
        }

        private static void DrawChartGrid(Canvas canvas, double width, double height)
        {
            for (int i = 1; i < 4; i++)
            {
                double y = i * height / 4.0d;
                WpfLine line = new WpfLine
                {
                    X1 = 0.0d,
                    X2 = width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(215, 224, 234)),
                    StrokeThickness = 1.0d
                };
                canvas.Children.Add(line);
            }
        }

        private static void AddChartMessage(Canvas canvas, string message)
        {
            TextBlock text = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(98, 112, 137)),
                FontSize = 13.0d
            };
            Canvas.SetLeft(text, 12.0d);
            Canvas.SetTop(text, 12.0d);
            canvas.Children.Add(text);
        }

        private static void AddChartScaleLabel(Canvas canvas, double min, double max)
        {
            TextBlock text = new TextBlock
            {
                Text = "min " + FormatNumber(min) + "   max " + FormatNumber(max),
                Foreground = new SolidColorBrush(Color.FromRgb(98, 112, 137)),
                FontSize = 11.0d
            };
            Canvas.SetLeft(text, 8.0d);
            Canvas.SetTop(text, 6.0d);
            canvas.Children.Add(text);
        }

        private static string BuildDefaultAnalyticsFileName(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return "measurement-session.Analytics.xlsx";
            }

            return Path.GetFileNameWithoutExtension(sourcePath) + ".Analytics.xlsx";
        }

        private static string FormatDuration(double? seconds)
        {
            if (!seconds.HasValue)
            {
                return "-";
            }

            TimeSpan duration = TimeSpan.FromSeconds(seconds.Value);
            return duration.ToString(duration.TotalHours >= 1.0d ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatEnergyValue(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.000000", CultureInfo.InvariantCulture) : "-";
        }

        private static string FormatNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : "-";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
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

        private sealed class SourceStatsRow
        {
            public string Source { get; set; }
            public string Mean { get; set; }
            public string StandardDeviation { get; set; }
            public string CoefficientOfVariation { get; set; }
            public string Drift { get; set; }
            public string Stationary { get; set; }
        }

        private sealed class MetricRow
        {
            public string Metric { get; set; }
            public string Value { get; set; }
        }

        private sealed class ChartSeries
        {
            public ChartSeries(string name, List<double?> values, Brush brush)
            {
                Name = name;
                Values = values;
                Brush = brush;
            }

            public string Name { get; private set; }
            public List<double?> Values { get; private set; }
            public Brush Brush { get; private set; }
        }
    }
}
