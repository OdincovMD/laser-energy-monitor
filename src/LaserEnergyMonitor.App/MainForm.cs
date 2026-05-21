using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.App
{
    public sealed class MainForm : Form
    {
        private readonly MeasurementSessionRuntimeFactory _runtimeFactory;
        private readonly string _defaultOutputDir;
        private MeasurementSessionService _service;
        private string _activeFirstSourceKey;
        private string _activeSecondSourceKey;

        private TextBox _sessionNameTextBox;
        private ComboBox _firstSourceComboBox;
        private ComboBox _secondSourceComboBox;
        private NumericUpDown _windowSizeInput;
        private NumericUpDown _enterThresholdInput;
        private NumericUpDown _exitThresholdInput;
        private NumericUpDown _syncDeltaInput;
        private TextBox _outputPathTextBox;
        private TextBox _sourceDiagnosticsTextBox;
        private Label _pairIdLabel;
        private Label _lastUpdatedLabel;
        private Label _stateLabel;
        private Label _firstEnergyLabel;
        private Label _secondEnergyLabel;
        private Label _firstAverageLabel;
        private Label _secondAverageLabel;
        private Label _metricLabel;
        private Label _stationaryLabel;
        private ListBox _eventsListBox;
        private Button _browseOutputButton;
        private Button _clearEventsButton;
        private Button _initializeButton;
        private Button _selfTestButton;
        private Button _startButton;
        private Button _stopButton;
        private DeviceStatusView _beamStatusView;
        private DeviceStatusView _ophirStatusView;

        public MainForm(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;
            Text = "Laser Energy Monitor";
            Width = 1240;
            Height = 860;
            MinimumSize = new Size(1160, 820);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(243, 246, 250);

            BuildLayout();
            WireEvents();
            UpdateState(MeasurementSessionState.Idle);
            RefreshSourceDiagnostics();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReplaceService(null);
            base.OnFormClosed(e);
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.Padding = new Padding(18);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 430));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            Panel headerPanel = BuildHeaderPanel();
            root.SetColumnSpan(headerPanel, 2);
            root.Controls.Add(headerPanel, 0, 0);

            GroupBox settingsGroup = new GroupBox();
            settingsGroup.Text = "Session Control";
            settingsGroup.Dock = DockStyle.Fill;
            settingsGroup.Padding = new Padding(12);
            settingsGroup.Font = new Font(Font, FontStyle.Bold);
            root.Controls.Add(settingsGroup, 0, 1);

            TableLayoutPanel settingsLayout = new TableLayoutPanel();
            settingsLayout.Dock = DockStyle.Fill;
            settingsLayout.ColumnCount = 2;
            settingsLayout.RowCount = 10;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 8; row++)
            {
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            }

            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            settingsGroup.Controls.Add(settingsLayout);

            _firstSourceComboBox = CreateSourceComboBox(_runtimeFactory.FirstSourceOptions, "beam-sim");
            _secondSourceComboBox = CreateSourceComboBox(_runtimeFactory.SecondSourceOptions, "ophir-sim");
            _sessionNameTextBox = new TextBox();
            _sessionNameTextBox.Text = "Prototype Session";
            _windowSizeInput = CreateNumeric(20, 5, 10000, 1);
            _enterThresholdInput = CreateNumeric(0.5m, 0.01m, 100m, 2);
            _exitThresholdInput = CreateNumeric(1.0m, 0.01m, 100m, 2);
            _syncDeltaInput = CreateNumeric(10m, 1m, 1000m, 0);
            _outputPathTextBox = new TextBox();
            _outputPathTextBox.Text = Path.Combine(_defaultOutputDir, "measurement-session.xlsx");
            _browseOutputButton = new Button();
            _browseOutputButton.Text = "Browse...";
            ApplySecondaryButtonStyle(_browseOutputButton);
            _sourceDiagnosticsTextBox = new TextBox();
            _sourceDiagnosticsTextBox.Multiline = true;
            _sourceDiagnosticsTextBox.ReadOnly = true;
            _sourceDiagnosticsTextBox.ScrollBars = ScrollBars.Vertical;
            _sourceDiagnosticsTextBox.BackColor = Color.White;
            _sourceDiagnosticsTextBox.BorderStyle = BorderStyle.FixedSingle;
            _sourceDiagnosticsTextBox.Font = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            _beamStatusView = CreateDeviceStatusView();
            _ophirStatusView = CreateDeviceStatusView();

            AddLabeledControl(settingsLayout, 0, "BeamGage Source", _firstSourceComboBox);
            AddLabeledControl(settingsLayout, 1, "Ophir Source", _secondSourceComboBox);
            AddLabeledControl(settingsLayout, 2, "Session Name", _sessionNameTextBox);
            AddLabeledControl(settingsLayout, 3, "Window N", _windowSizeInput);
            AddLabeledControl(settingsLayout, 4, "Enter Threshold %", _enterThresholdInput);
            AddLabeledControl(settingsLayout, 5, "Exit Threshold %", _exitThresholdInput);
            AddLabeledControl(settingsLayout, 6, "Sync Delta ms", _syncDeltaInput);
            AddLabeledControl(settingsLayout, 7, "Output Path", BuildOutputPathPanel());
            AddLabeledControl(settingsLayout, 8, "Source Status", BuildDiagnosticsTabs());

            TableLayoutPanel buttonsPanel = new TableLayoutPanel();
            buttonsPanel.Dock = DockStyle.Fill;
            buttonsPanel.ColumnCount = 2;
            buttonsPanel.RowCount = 2;
            buttonsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            buttonsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            _initializeButton = new Button();
            _initializeButton.Text = "Initialize";
            _selfTestButton = new Button();
            _selfTestButton.Text = "Run Self-Test";
            _startButton = new Button();
            _startButton.Text = "Start";
            _stopButton = new Button();
            _stopButton.Text = "Stop";

            ApplyPrimaryButtonStyle(_initializeButton, false);
            ApplyPrimaryButtonStyle(_selfTestButton, false);
            ApplyPrimaryButtonStyle(_startButton, true);
            ApplyPrimaryButtonStyle(_stopButton, false);

            buttonsPanel.Controls.Add(_initializeButton, 0, 0);
            buttonsPanel.Controls.Add(_selfTestButton, 1, 0);
            buttonsPanel.Controls.Add(_startButton, 0, 1);
            buttonsPanel.Controls.Add(_stopButton, 1, 1);
            AddLabeledControl(settingsLayout, 9, "Actions", buttonsPanel);

            GroupBox liveGroup = new GroupBox();
            liveGroup.Text = "Live Status";
            liveGroup.Dock = DockStyle.Fill;
            liveGroup.Padding = new Padding(12);
            liveGroup.Font = new Font(Font, FontStyle.Bold);
            root.Controls.Add(liveGroup, 1, 1);

            TableLayoutPanel liveLayout = new TableLayoutPanel();
            liveLayout.Dock = DockStyle.Fill;
            liveLayout.ColumnCount = 2;
            liveLayout.RowCount = 9;
            liveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            liveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 9; row++)
            {
                liveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            }

            liveGroup.Controls.Add(liveLayout);

            _stateLabel = new Label();
            _stateLabel.AutoSize = false;
            _stateLabel.TextAlign = ContentAlignment.MiddleLeft;
            _stateLabel.BackColor = Color.FromArgb(229, 236, 246);
            _stateLabel.BorderStyle = BorderStyle.FixedSingle;
            _stateLabel.Padding = new Padding(8, 0, 8, 0);
            _stateLabel.Height = 30;
            _pairIdLabel = new Label();
            _lastUpdatedLabel = new Label();
            _firstEnergyLabel = new Label();
            _secondEnergyLabel = new Label();
            _firstAverageLabel = new Label();
            _secondAverageLabel = new Label();
            _metricLabel = new Label();
            _stationaryLabel = new Label();

            AddLabeledValue(liveLayout, 0, "State", _stateLabel);
            AddLabeledValue(liveLayout, 1, "Pair ID", _pairIdLabel);
            AddLabeledValue(liveLayout, 2, "Last Update", _lastUpdatedLabel);
            AddLabeledValue(liveLayout, 3, "Beam Gage Energy", _firstEnergyLabel);
            AddLabeledValue(liveLayout, 4, "Ophir Energy", _secondEnergyLabel);
            AddLabeledValue(liveLayout, 5, "Beam Gage Average", _firstAverageLabel);
            AddLabeledValue(liveLayout, 6, "Ophir Average", _secondAverageLabel);
            AddLabeledValue(liveLayout, 7, "Stability Metric", _metricLabel);
            AddLabeledValue(liveLayout, 8, "Stationary", _stationaryLabel);

            GroupBox eventsGroup = new GroupBox();
            eventsGroup.Text = "Events";
            eventsGroup.Dock = DockStyle.Fill;
            eventsGroup.Padding = new Padding(12);
            eventsGroup.Font = new Font(Font, FontStyle.Bold);
            root.SetColumnSpan(eventsGroup, 2);
            root.Controls.Add(eventsGroup, 0, 2);

            TableLayoutPanel eventsLayout = new TableLayoutPanel();
            eventsLayout.Dock = DockStyle.Fill;
            eventsLayout.ColumnCount = 1;
            eventsLayout.RowCount = 2;
            eventsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            eventsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            eventsGroup.Controls.Add(eventsLayout);

            FlowLayoutPanel eventsToolbar = new FlowLayoutPanel();
            eventsToolbar.Dock = DockStyle.Fill;
            eventsToolbar.FlowDirection = FlowDirection.RightToLeft;
            eventsToolbar.WrapContents = false;
            _clearEventsButton = new Button();
            _clearEventsButton.Text = "Clear Log";
            _clearEventsButton.Width = 92;
            ApplySecondaryButtonStyle(_clearEventsButton);
            _clearEventsButton.Dock = DockStyle.None;
            eventsToolbar.Controls.Add(_clearEventsButton);
            eventsLayout.Controls.Add(eventsToolbar, 0, 0);

            _eventsListBox = new ListBox();
            _eventsListBox.Dock = DockStyle.Fill;
            _eventsListBox.Font = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            eventsLayout.Controls.Add(_eventsListBox, 0, 1);
        }

        private void WireEvents()
        {
            _initializeButton.Click += OnInitializeClicked;
            _selfTestButton.Click += OnSelfTestClicked;
            _startButton.Click += OnStartClicked;
            _stopButton.Click += OnStopClicked;
            _firstSourceComboBox.SelectedIndexChanged += OnSourceSelectionChanged;
            _secondSourceComboBox.SelectedIndexChanged += OnSourceSelectionChanged;
            _browseOutputButton.Click += OnBrowseOutputClicked;
            _clearEventsButton.Click += OnClearEventsClicked;
        }

        private void OnInitializeClicked(object sender, EventArgs e)
        {
            try
            {
                EnsureInitializedService(BuildSettings(), true);
                AddEvent("Sources initialized.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Initialization error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnStartClicked(object sender, EventArgs e)
        {
            try
            {
                MeasurementSessionService service = EnsureInitializedService(BuildSettings(), false);
                service.Start();
                AddEvent("Session started.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Start error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSelfTestClicked(object sender, EventArgs e)
        {
            try
            {
                string report = _runtimeFactory.RunSelfTest(
                    GetSelectedSourceKey(_firstSourceComboBox),
                    GetSelectedSourceKey(_secondSourceComboBox));

                _sourceDiagnosticsTextBox.Text = report;
                RefreshSourceDiagnostics();
                AddEvent("Hardware self-test completed.");
                MessageBox.Show(report, "Hardware Self-Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Self-test error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnStopClicked(object sender, EventArgs e)
        {
            try
            {
                if (_service == null)
                {
                    return;
                }

                _service.Stop();
                AddEvent("Session stopped.");
                ResetLiveValues();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Stop error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSourceSelectionChanged(object sender, EventArgs e)
        {
            RefreshSourceDiagnostics();
        }

        private void OnBrowseOutputClicked(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*";
                dialog.Title = "Select export file";
                dialog.FileName = Path.GetFileName(_outputPathTextBox.Text);
                string directory = Path.GetDirectoryName(_outputPathTextBox.Text);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    dialog.InitialDirectory = directory;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _outputPathTextBox.Text = dialog.FileName;
                }
            }
        }

        private void OnClearEventsClicked(object sender, EventArgs e)
        {
            _eventsListBox.Items.Clear();
            AddEvent("Event log cleared.");
        }

        private void OnStateChanged(object sender, SessionStateChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, SessionStateChangedEventArgs>(OnStateChanged), sender, e);
                return;
            }

            UpdateState(e.State);
        }

        private void OnLiveMeasurementUpdated(object sender, LiveMeasurementUpdatedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, LiveMeasurementUpdatedEventArgs>(OnLiveMeasurementUpdated), sender, e);
                return;
            }

            _firstEnergyLabel.Text = FormatDouble(e.Snapshot.FirstEnergy);
            _secondEnergyLabel.Text = FormatDouble(e.Snapshot.SecondEnergy);
            _firstAverageLabel.Text = FormatDouble(e.Snapshot.FirstAverage);
            _secondAverageLabel.Text = FormatDouble(e.Snapshot.SecondAverage);
            _metricLabel.Text = FormatDouble(e.Snapshot.StabilityMetric);
            _stationaryLabel.Text = e.Snapshot.IsStationary ? "Yes" : "No";
            _pairIdLabel.Text = e.Snapshot.PairId.ToString(CultureInfo.InvariantCulture);
            _lastUpdatedLabel.Text = e.Snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private void OnSessionEventRaised(object sender, SessionEventRaisedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, SessionEventRaisedEventArgs>(OnSessionEventRaised), sender, e);
                return;
            }

            SessionEvent sessionEvent = e.SessionEvent;
            AddEvent(string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss} [{1}] {2}",
                sessionEvent.TimestampUtc.ToLocalTime(),
                sessionEvent.EventType,
                sessionEvent.Message));
        }

        private SessionSettings BuildSettings()
        {
            return new SessionSettings
            {
                SessionName = _sessionNameTextBox.Text,
                RollingWindowSize = Decimal.ToInt32(_windowSizeInput.Value),
                EnterThresholdPercent = Decimal.ToDouble(_enterThresholdInput.Value),
                ExitThresholdPercent = Decimal.ToDouble(_exitThresholdInput.Value),
                SynchronizationDelta = TimeSpan.FromMilliseconds(Decimal.ToDouble(_syncDeltaInput.Value)),
                OutputPath = _outputPathTextBox.Text
            };
        }

        private void UpdateState(MeasurementSessionState state)
        {
            _stateLabel.Text = state.ToString();
            _stateLabel.ForeColor = GetStateColor(state);
            _startButton.Enabled = state == MeasurementSessionState.Idle || state == MeasurementSessionState.Initialized || state == MeasurementSessionState.Completed;
            _stopButton.Enabled = state == MeasurementSessionState.Measuring || state == MeasurementSessionState.Stationary || state == MeasurementSessionState.Faulted;
            if (state == MeasurementSessionState.Idle)
            {
                ResetLiveValues();
            }
        }

        private void AddEvent(string message)
        {
            _eventsListBox.Items.Insert(0, message);
            while (_eventsListBox.Items.Count > 500)
            {
                _eventsListBox.Items.RemoveAt(_eventsListBox.Items.Count - 1);
            }
        }

        private MeasurementSessionService EnsureInitializedService(SessionSettings settings, bool forceRecreate)
        {
            string firstKey = GetSelectedSourceKey(_firstSourceComboBox);
            string secondKey = GetSelectedSourceKey(_secondSourceComboBox);
            bool selectionChanged =
                !string.Equals(firstKey, _activeFirstSourceKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(secondKey, _activeSecondSourceKey, StringComparison.OrdinalIgnoreCase);

            if (!forceRecreate && !selectionChanged && _service != null && _service.State == MeasurementSessionState.Initialized)
            {
                return _service;
            }

            MeasurementSessionService newService = _runtimeFactory.Create(firstKey, secondKey);
            try
            {
                newService.Initialize(settings);
                ReplaceService(newService);
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

        private void ReplaceService(MeasurementSessionService newService)
        {
            if (_service != null)
            {
                _service.StateChanged -= OnStateChanged;
                _service.LiveMeasurementUpdated -= OnLiveMeasurementUpdated;
                _service.SessionEventRaised -= OnSessionEventRaised;
                _service.Dispose();
            }

            _service = newService;

            if (_service != null)
            {
                _service.StateChanged += OnStateChanged;
                _service.LiveMeasurementUpdated += OnLiveMeasurementUpdated;
                _service.SessionEventRaised += OnSessionEventRaised;
            }
            else
            {
                ResetLiveValues();
            }
        }

        private void RefreshSourceDiagnostics()
        {
            if (_sourceDiagnosticsTextBox == null || _beamStatusView == null || _ophirStatusView == null)
            {
                return;
            }

            string firstSourceKey = GetSelectedSourceKey(_firstSourceComboBox);
            string secondSourceKey = GetSelectedSourceKey(_secondSourceComboBox);
            IReadOnlyList<MeasurementSourceDiagnostic> diagnostics = _runtimeFactory.GetDiagnostics(firstSourceKey, secondSourceKey);

            _sourceDiagnosticsTextBox.Text = _runtimeFactory.BuildDiagnostics(firstSourceKey, secondSourceKey);
            if (diagnostics.Count > 0)
            {
                BindDeviceStatus(_beamStatusView, diagnostics[0]);
            }

            if (diagnostics.Count > 1)
            {
                BindDeviceStatus(_ophirStatusView, diagnostics[1]);
            }
        }

        private Control BuildOutputPathPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 2;
            panel.RowCount = 1;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            panel.Controls.Add(_outputPathTextBox, 0, 0);
            panel.Controls.Add(_browseOutputButton, 1, 0);
            return panel;
        }

        private Control BuildDiagnosticsTabs()
        {
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;

            TabPage overviewPage = new TabPage("Overview");
            overviewPage.BackColor = Color.White;
            overviewPage.Padding = new Padding(8);

            TableLayoutPanel overviewLayout = new TableLayoutPanel();
            overviewLayout.Dock = DockStyle.Fill;
            overviewLayout.ColumnCount = 1;
            overviewLayout.RowCount = 2;
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            overviewLayout.Controls.Add(_beamStatusView.Root, 0, 0);
            overviewLayout.Controls.Add(_ophirStatusView.Root, 0, 1);
            overviewPage.Controls.Add(overviewLayout);

            TabPage reportPage = new TabPage("Report");
            reportPage.BackColor = Color.White;
            reportPage.Padding = new Padding(8);
            _sourceDiagnosticsTextBox.Dock = DockStyle.Fill;
            reportPage.Controls.Add(_sourceDiagnosticsTextBox);

            tabs.TabPages.Add(overviewPage);
            tabs.TabPages.Add(reportPage);
            return tabs;
        }

        private static DeviceStatusView CreateDeviceStatusView()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Color.White;
            root.BorderStyle = BorderStyle.FixedSingle;
            root.Padding = new Padding(12);
            root.Margin = new Padding(0, 0, 0, 8);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.Controls.Add(layout);

            Label titleLabel = new Label();
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font("Segoe UI Semibold", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(28, 39, 56);

            Label summaryLabel = new Label();
            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.AutoEllipsis = true;
            summaryLabel.ForeColor = Color.FromArgb(52, 63, 78);

            Label acquisitionLabel = new Label();
            acquisitionLabel.Dock = DockStyle.Fill;
            acquisitionLabel.ForeColor = Color.FromArgb(92, 104, 120);

            ListBox stepsListBox = new ListBox();
            stepsListBox.Dock = DockStyle.Fill;
            stepsListBox.BorderStyle = BorderStyle.FixedSingle;
            stepsListBox.Font = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            stepsListBox.IntegralHeight = false;

            TextBox detailsTextBox = new TextBox();
            detailsTextBox.Dock = DockStyle.Fill;
            detailsTextBox.Multiline = true;
            detailsTextBox.ReadOnly = true;
            detailsTextBox.BorderStyle = BorderStyle.None;
            detailsTextBox.BackColor = Color.White;
            detailsTextBox.ForeColor = Color.FromArgb(92, 104, 120);
            detailsTextBox.ScrollBars = ScrollBars.Vertical;

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(summaryLabel, 0, 1);
            layout.Controls.Add(acquisitionLabel, 0, 2);
            layout.Controls.Add(stepsListBox, 0, 3);
            layout.Controls.Add(detailsTextBox, 0, 4);

            return new DeviceStatusView
            {
                Root = root,
                TitleLabel = titleLabel,
                SummaryLabel = summaryLabel,
                AcquisitionLabel = acquisitionLabel,
                StepsListBox = stepsListBox,
                DetailsTextBox = detailsTextBox
            };
        }

        private static void BindDeviceStatus(DeviceStatusView view, MeasurementSourceDiagnostic diagnostic)
        {
            MeasurementSourceRuntimeProbeResult probe = diagnostic != null ? diagnostic.Probe : null;
            bool dependencyAvailable = probe != null && probe.DependencyAvailable;

            view.Root.BackColor = dependencyAvailable ? Color.FromArgb(248, 252, 248) : Color.FromArgb(253, 245, 244);
            view.TitleLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1}",
                diagnostic != null ? diagnostic.SlotName : "Source",
                diagnostic != null ? diagnostic.DisplayName : "Unavailable");
            view.TitleLabel.ForeColor = dependencyAvailable ? Color.FromArgb(19, 92, 52) : Color.FromArgb(156, 43, 32);
            view.SummaryLabel.Text = probe != null ? probe.Summary : "No diagnostic data available.";
            view.AcquisitionLabel.Text =
                diagnostic != null && diagnostic.IsImplemented
                    ? "Live acquisition is available in this build."
                    : "Live acquisition is not wired in this build.";

            view.StepsListBox.Items.Clear();
            if (probe != null && probe.Steps != null && probe.Steps.Count > 0)
            {
                for (int i = 0; i < probe.Steps.Count; i++)
                {
                    MeasurementSourceRuntimeProbeStep step = probe.Steps[i];
                    view.StepsListBox.Items.Add(FormatStep(step));
                }
            }
            else
            {
                view.StepsListBox.Items.Add("No detailed probe steps were reported.");
            }

            view.DetailsTextBox.Text = probe != null ? probe.Details : string.Empty;
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

        private void ResetLiveValues()
        {
            _pairIdLabel.Text = "-";
            _lastUpdatedLabel.Text = "-";
            _firstEnergyLabel.Text = "-";
            _secondEnergyLabel.Text = "-";
            _firstAverageLabel.Text = "-";
            _secondAverageLabel.Text = "-";
            _metricLabel.Text = "-";
            _stationaryLabel.Text = "-";
        }

        private static ComboBox CreateSourceComboBox(
            System.Collections.Generic.IReadOnlyList<MeasurementSourceOption> options,
            string defaultKey)
        {
            ComboBox comboBox = new ComboBox();
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;

            for (int i = 0; i < options.Count; i++)
            {
                comboBox.Items.Add(options[i]);
                if (string.Equals(options[i].Key, defaultKey, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                }
            }

            if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }

            return comboBox;
        }

        private static string GetSelectedSourceKey(ComboBox comboBox)
        {
            MeasurementSourceOption option = comboBox != null ? comboBox.SelectedItem as MeasurementSourceOption : null;
            return option != null ? option.Key : string.Empty;
        }

        private static NumericUpDown CreateNumeric(decimal value, decimal minimum, decimal maximum, int decimalPlaces)
        {
            NumericUpDown input = new NumericUpDown();
            input.DecimalPlaces = decimalPlaces;
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = value;
            input.Width = 120;
            return input;
        }

        private static void AddLabeledControl(TableLayoutPanel layout, int row, string labelText, Control control)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(3, 8, 10, 8);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(0, 4, 0, 4);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private static void AddLabeledValue(TableLayoutPanel layout, int row, string labelText, Label valueLabel)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(3, 10, 10, 10);
            valueLabel.AutoSize = false;
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.Text = "-";
            valueLabel.Font = new Font("Segoe UI Semibold", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(valueLabel, 1, row);
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "-";
        }

        private Panel BuildHeaderPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.White;
            panel.Padding = new Padding(18, 14, 18, 12);
            panel.Margin = new Padding(0, 0, 0, 12);

            Label titleLabel = new Label();
            titleLabel.Text = "Laser Energy Monitor";
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 28;
            titleLabel.Font = new Font("Segoe UI", 16.0f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(28, 39, 56);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "Configure sources, run hardware diagnostics, and monitor the live session state from one screen.";
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            subtitleLabel.ForeColor = Color.FromArgb(92, 104, 120);

            panel.Controls.Add(subtitleLabel);
            panel.Controls.Add(titleLabel);
            return panel;
        }

        private static void ApplyPrimaryButtonStyle(Button button, bool accent)
        {
            button.Dock = DockStyle.Fill;
            button.AutoSize = false;
            button.Height = 38;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Margin = new Padding(4);
            button.Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold, GraphicsUnit.Point);
            button.BackColor = accent ? Color.FromArgb(47, 109, 214) : Color.FromArgb(230, 236, 244);
            button.ForeColor = accent ? Color.White : Color.FromArgb(33, 42, 53);
        }

        private static void ApplySecondaryButtonStyle(Button button)
        {
            button.Dock = DockStyle.Fill;
            button.AutoSize = false;
            button.Height = 30;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 214, 226);
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(33, 42, 53);
            button.Margin = new Padding(4);
        }

        private static Color GetStateColor(MeasurementSessionState state)
        {
            switch (state)
            {
                case MeasurementSessionState.Initialized:
                    return Color.FromArgb(17, 90, 153);
                case MeasurementSessionState.Measuring:
                    return Color.FromArgb(16, 124, 16);
                case MeasurementSessionState.Stationary:
                    return Color.FromArgb(114, 76, 0);
                case MeasurementSessionState.Faulted:
                    return Color.FromArgb(180, 35, 24);
                case MeasurementSessionState.Completed:
                    return Color.FromArgb(90, 90, 90);
                default:
                    return Color.FromArgb(52, 63, 78);
            }
        }

        private sealed class DeviceStatusView
        {
            public Panel Root { get; set; }

            public Label TitleLabel { get; set; }

            public Label SummaryLabel { get; set; }

            public Label AcquisitionLabel { get; set; }

            public ListBox StepsListBox { get; set; }

            public TextBox DetailsTextBox { get; set; }
        }
    }
}
