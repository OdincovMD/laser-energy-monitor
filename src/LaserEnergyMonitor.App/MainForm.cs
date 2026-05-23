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
        private Label _stateMirrorLabel;
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
        private Button _ophirSmokeTestButton;
        private Button _startButton;
        private Button _stopButton;
        private DeviceStatusView _beamStatusView;
        private DeviceStatusView _ophirStatusView;

        public MainForm(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;
            Text = "Laser Energy Monitor";
            Width = 1440;
            Height = 940;
            MinimumSize = new Size(1280, 860);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(239, 243, 248);

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
            SuspendLayout();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(24);
            root.BackColor = BackColor;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            Controls.Add(root);

            root.Controls.Add(BuildHeaderPanel(), 0, 0);

            TableLayoutPanel mainContentLayout = new TableLayoutPanel();
            mainContentLayout.Dock = DockStyle.Fill;
            mainContentLayout.ColumnCount = 2;
            mainContentLayout.RowCount = 1;
            mainContentLayout.Margin = new Padding(0, 18, 0, 0);
            mainContentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            mainContentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            root.Controls.Add(mainContentLayout, 0, 1);

            SectionCardView settingsCard = CreateSectionCard(
                "Session Control",
                "Configure the run, verify source health, and launch diagnostics from a single control surface.");
            settingsCard.Root.Margin = new Padding(0, 0, 12, 12);
            settingsCard.Root.MinimumSize = new Size(480, 0);
            mainContentLayout.Controls.Add(settingsCard.Root, 0, 0);

            Panel settingsScrollPanel = new Panel();
            settingsScrollPanel.Dock = DockStyle.Fill;
            settingsScrollPanel.AutoScroll = true;
            settingsScrollPanel.Padding = new Padding(0, 2, 4, 0);
            settingsCard.Body.Controls.Add(settingsScrollPanel);

            TableLayoutPanel settingsLayout = new TableLayoutPanel();
            settingsLayout.Dock = DockStyle.Top;
            settingsLayout.AutoSize = true;
            settingsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            settingsLayout.ColumnCount = 2;
            settingsLayout.RowCount = 10;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 8; row++)
            {
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 288));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsScrollPanel.Controls.Add(settingsLayout);

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
            _sourceDiagnosticsTextBox.Font = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            _beamStatusView = CreateDeviceStatusView();
            _ophirStatusView = CreateDeviceStatusView();

            ApplyInputStyle(_sessionNameTextBox);
            ApplyInputStyle(_firstSourceComboBox);
            ApplyInputStyle(_secondSourceComboBox);
            ApplyInputStyle(_windowSizeInput);
            ApplyInputStyle(_enterThresholdInput);
            ApplyInputStyle(_exitThresholdInput);
            ApplyInputStyle(_syncDeltaInput);
            ApplyInputStyle(_outputPathTextBox);
            ApplyReadOnlyTextStyle(_sourceDiagnosticsTextBox);

            AddLabeledControl(settingsLayout, 0, "BeamGage Source", _firstSourceComboBox);
            AddLabeledControl(settingsLayout, 1, "Ophir Source", _secondSourceComboBox);
            AddLabeledControl(settingsLayout, 2, "Session Name", _sessionNameTextBox);
            AddLabeledControl(settingsLayout, 3, "Window N", _windowSizeInput);
            AddLabeledControl(settingsLayout, 4, "Enter Threshold %", _enterThresholdInput);
            AddLabeledControl(settingsLayout, 5, "Exit Threshold %", _exitThresholdInput);
            AddLabeledControl(settingsLayout, 6, "Sync Delta ms", _syncDeltaInput);
            AddLabeledControl(settingsLayout, 7, "Output Path", BuildOutputPathPanel());
            AddLabeledControl(settingsLayout, 8, "Source Status", BuildDiagnosticsTabs());

            FlowLayoutPanel buttonsPanel = new FlowLayoutPanel();
            buttonsPanel.Dock = DockStyle.Fill;
            buttonsPanel.AutoSize = true;
            buttonsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonsPanel.WrapContents = true;
            buttonsPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonsPanel.Margin = new Padding(0);
            buttonsPanel.Padding = new Padding(0);

            _initializeButton = new Button();
            _initializeButton.Text = "Initialize";
            _selfTestButton = new Button();
            _selfTestButton.Text = "Run Self-Test";
            _ophirSmokeTestButton = new Button();
            _ophirSmokeTestButton.Text = "Ophir Smoke-Test";
            _startButton = new Button();
            _startButton.Text = "Start";
            _stopButton = new Button();
            _stopButton.Text = "Stop";

            ApplyPrimaryButtonStyle(_initializeButton, false);
            ApplyPrimaryButtonStyle(_selfTestButton, false);
            ApplyPrimaryButtonStyle(_ophirSmokeTestButton, false);
            ApplyPrimaryButtonStyle(_startButton, true);
            ApplyPrimaryButtonStyle(_stopButton, false);

            buttonsPanel.Controls.Add(_initializeButton);
            buttonsPanel.Controls.Add(_selfTestButton);
            buttonsPanel.Controls.Add(_ophirSmokeTestButton);
            buttonsPanel.Controls.Add(_startButton);
            buttonsPanel.Controls.Add(_stopButton);
            AddLabeledControl(settingsLayout, 9, "Actions", buttonsPanel);

            SectionCardView liveCard = CreateSectionCard(
                "Live Status",
                "Keep the session state, pair timing, and energy metrics visible without fighting the layout.");
            liveCard.Root.Margin = new Padding(12, 0, 0, 12);
            mainContentLayout.Controls.Add(liveCard.Root, 1, 0);

            TableLayoutPanel liveLayout = new TableLayoutPanel();
            liveLayout.Dock = DockStyle.Fill;
            liveLayout.ColumnCount = 1;
            liveLayout.RowCount = 2;
            liveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            liveLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            liveCard.Body.Controls.Add(liveLayout);

            _stateLabel = new Label();
            _stateLabel.AutoSize = false;
            _stateLabel.Dock = DockStyle.Left;
            _stateLabel.Width = 180;
            _stateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _stateLabel.Padding = new Padding(14, 0, 14, 0);
            _stateLabel.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Bold, GraphicsUnit.Point);

            _pairIdLabel = new Label();
            _lastUpdatedLabel = new Label();
            _firstEnergyLabel = new Label();
            _secondEnergyLabel = new Label();
            _firstAverageLabel = new Label();
            _secondAverageLabel = new Label();
            _metricLabel = new Label();
            _stationaryLabel = new Label();

            liveLayout.Controls.Add(BuildStateBanner(), 0, 0);
            liveLayout.Controls.Add(BuildMetricsGrid(), 0, 1);

            SectionCardView eventsCard = CreateSectionCard(
                "Events",
                "Recent operator and session activity stays visible at the bottom of the workspace.");
            eventsCard.Root.Margin = new Padding(0, 18, 0, 0);
            root.Controls.Add(eventsCard.Root, 0, 2);

            TableLayoutPanel eventsLayout = new TableLayoutPanel();
            eventsLayout.Dock = DockStyle.Fill;
            eventsLayout.ColumnCount = 1;
            eventsLayout.RowCount = 2;
            eventsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            eventsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            eventsCard.Body.Controls.Add(eventsLayout);

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
            _eventsListBox.BorderStyle = BorderStyle.None;
            _eventsListBox.BackColor = Color.FromArgb(249, 251, 253);
            _eventsListBox.ForeColor = Color.FromArgb(37, 45, 58);
            _eventsListBox.HorizontalScrollbar = true;
            eventsLayout.Controls.Add(_eventsListBox, 0, 1);

            ResumeLayout(true);
        }

        private void WireEvents()
        {
            _initializeButton.Click += OnInitializeClicked;
            _selfTestButton.Click += OnSelfTestClicked;
            _ophirSmokeTestButton.Click += OnOphirSmokeTestClicked;
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

        private void OnOphirSmokeTestClicked(object sender, EventArgs e)
        {
            try
            {
                string report = _runtimeFactory.RunOphirSmokeTest(GetSelectedSourceKey(_secondSourceComboBox));
                _sourceDiagnosticsTextBox.Text = report;
                AddEvent("Ophir smoke-test completed.");
                MessageBox.Show(report, "Ophir Smoke-Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ophir smoke-test error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _stateLabel.BackColor = GetStateBackColor(state);
            if (_stateMirrorLabel != null)
            {
                _stateMirrorLabel.Text = state.ToString();
                _stateMirrorLabel.ForeColor = GetStateColor(state);
            }

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
            tabs.Appearance = TabAppearance.Normal;
            tabs.Padding = new Point(16, 6);
            tabs.ItemSize = new Size(140, 30);
            tabs.SizeMode = TabSizeMode.Fixed;

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
            root.BackColor = Color.FromArgb(250, 252, 255);
            root.BorderStyle = BorderStyle.FixedSingle;
            root.Padding = new Padding(14);
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
            stepsListBox.BorderStyle = BorderStyle.None;
            stepsListBox.BackColor = Color.White;
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
            if (_stateMirrorLabel != null)
            {
                _stateMirrorLabel.Text = "Idle";
            }
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
            return input;
        }

        private static void AddLabeledControl(TableLayoutPanel layout, int row, string labelText, Control control)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold, GraphicsUnit.Point);
            label.ForeColor = Color.FromArgb(58, 69, 82);
            label.Margin = new Padding(0, 12, 12, 8);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            control.Margin = new Padding(0, 8, 0, 8);
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
            panel.BackColor = Color.FromArgb(20, 34, 54);
            panel.Padding = new Padding(24, 20, 24, 18);
            panel.Margin = new Padding(0);

            Label titleLabel = new Label();
            titleLabel.Text = "Laser Energy Monitor";
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 34;
            titleLabel.Font = new Font("Segoe UI Semibold", 19.0f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.White;

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "Configure sources, run hardware diagnostics, and monitor the live session state from one screen.";
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.Font = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            subtitleLabel.ForeColor = Color.FromArgb(204, 215, 228);

            panel.Controls.Add(subtitleLabel);
            panel.Controls.Add(titleLabel);
            return panel;
        }

        private static void ApplyPrimaryButtonStyle(Button button, bool accent)
        {
            button.AutoSize = false;
            button.Width = accent ? 170 : 152;
            button.Height = 40;
            button.MinimumSize = new Size(148, 40);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Margin = new Padding(0, 0, 12, 12);
            button.Padding = new Padding(12, 0, 12, 0);
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            button.BackColor = accent ? Color.FromArgb(31, 104, 210) : Color.FromArgb(230, 235, 241);
            button.ForeColor = accent ? Color.White : Color.FromArgb(35, 44, 56);
        }

        private static void ApplySecondaryButtonStyle(Button button)
        {
            button.AutoSize = false;
            button.Height = 36;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(208, 216, 227);
            button.FlatAppearance.BorderSize = 1;
            button.Padding = new Padding(10, 0, 10, 0);
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold, GraphicsUnit.Point);
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(33, 42, 53);
            button.Margin = new Padding(4);
        }

        private Control BuildStateBanner()
        {
            Panel banner = new Panel();
            banner.Dock = DockStyle.Fill;
            banner.Padding = new Padding(18);
            banner.BackColor = Color.FromArgb(245, 248, 252);
            banner.BorderStyle = BorderStyle.FixedSingle;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            banner.Controls.Add(layout);

            layout.Controls.Add(_stateLabel, 0, 0);

            TableLayoutPanel textLayout = new TableLayoutPanel();
            textLayout.Dock = DockStyle.Fill;
            textLayout.ColumnCount = 1;
            textLayout.RowCount = 2;
            textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(textLayout, 1, 0);

            Label titleLabel = new Label();
            titleLabel.Text = "Session Overview";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(29, 41, 55);

            Label descriptionLabel = new Label();
            descriptionLabel.Text = "The live board below stays aligned while the session moves between initialization, acquisition, and stationary detection.";
            descriptionLabel.Dock = DockStyle.Fill;
            descriptionLabel.ForeColor = Color.FromArgb(92, 104, 120);

            textLayout.Controls.Add(titleLabel, 0, 0);
            textLayout.Controls.Add(descriptionLabel, 0, 1);
            return banner;
        }

        private Control BuildMetricsGrid()
        {
            TableLayoutPanel metricsLayout = new TableLayoutPanel();
            metricsLayout.Dock = DockStyle.Fill;
            metricsLayout.ColumnCount = 3;
            metricsLayout.RowCount = 3;
            metricsLayout.Padding = new Padding(0, 16, 0, 0);
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));

            metricsLayout.Controls.Add(CreateMetricCard("Pair ID", _pairIdLabel), 0, 0);
            metricsLayout.Controls.Add(CreateMetricCard("Last Update", _lastUpdatedLabel), 1, 0);
            metricsLayout.Controls.Add(CreateMetricCard("Stationary", _stationaryLabel), 2, 0);
            metricsLayout.Controls.Add(CreateMetricCard("Beam Gage Energy", _firstEnergyLabel), 0, 1);
            metricsLayout.Controls.Add(CreateMetricCard("Ophir Energy", _secondEnergyLabel), 1, 1);
            metricsLayout.Controls.Add(CreateMetricCard("Stability Metric", _metricLabel), 2, 1);
            metricsLayout.Controls.Add(CreateMetricCard("Beam Gage Average", _firstAverageLabel), 0, 2);
            metricsLayout.Controls.Add(CreateMetricCard("Ophir Average", _secondAverageLabel), 1, 2);
            _stateMirrorLabel = CreateMirrorValueLabel();
            metricsLayout.Controls.Add(CreateMetricCard("State Mirror", _stateMirrorLabel), 2, 2);
            return metricsLayout;
        }

        private Label CreateMirrorValueLabel()
        {
            Label label = new Label();
            label.Text = "Idle";
            return label;
        }

        private Control CreateMetricCard(string title, Label valueLabel)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16, 14, 16, 14);
            card.Margin = new Padding(0, 0, 12, 12);
            card.BackColor = Color.White;
            card.BorderStyle = BorderStyle.FixedSingle;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 24;
            titleLabel.Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(99, 111, 126);

            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Text = "-";
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.Font = new Font("Segoe UI Semibold", 14.0f, FontStyle.Bold, GraphicsUnit.Point);
            valueLabel.ForeColor = Color.FromArgb(27, 37, 49);
            valueLabel.Margin = new Padding(0);

            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            return card;
        }

        private static void ApplyInputStyle(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 8, 0, 8);
            control.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            TextBox textBox = control as TextBox;
            if (textBox != null)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = Color.White;
                textBox.ForeColor = Color.FromArgb(33, 42, 53);
                return;
            }

            ComboBox comboBox = control as ComboBox;
            if (comboBox != null)
            {
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = Color.White;
                comboBox.ForeColor = Color.FromArgb(33, 42, 53);
                return;
            }

            NumericUpDown numeric = control as NumericUpDown;
            if (numeric != null)
            {
                numeric.BorderStyle = BorderStyle.FixedSingle;
                numeric.BackColor = Color.White;
                numeric.ForeColor = Color.FromArgb(33, 42, 53);
            }
        }

        private static void ApplyReadOnlyTextStyle(TextBox textBox)
        {
            textBox.BackColor = Color.FromArgb(249, 251, 253);
            textBox.ForeColor = Color.FromArgb(38, 47, 59);
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static SectionCardView CreateSectionCard(string title, string subtitle)
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(20);
            root.BackColor = Color.White;
            root.BorderStyle = BorderStyle.FixedSingle;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(layout);

            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Fill;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 30;
            titleLabel.Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(24, 35, 48);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = subtitle;
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.ForeColor = Color.FromArgb(103, 114, 128);

            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(titleLabel);
            layout.Controls.Add(headerPanel, 0, 0);

            Panel bodyPanel = new Panel();
            bodyPanel.Dock = DockStyle.Fill;
            layout.Controls.Add(bodyPanel, 0, 1);

            return new SectionCardView
            {
                Root = root,
                Body = bodyPanel
            };
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

        private static Color GetStateBackColor(MeasurementSessionState state)
        {
            switch (state)
            {
                case MeasurementSessionState.Initialized:
                    return Color.FromArgb(226, 239, 255);
                case MeasurementSessionState.Measuring:
                    return Color.FromArgb(225, 245, 231);
                case MeasurementSessionState.Stationary:
                    return Color.FromArgb(255, 244, 214);
                case MeasurementSessionState.Faulted:
                    return Color.FromArgb(255, 228, 224);
                case MeasurementSessionState.Completed:
                    return Color.FromArgb(233, 236, 240);
                default:
                    return Color.FromArgb(230, 236, 244);
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

        private sealed class SectionCardView
        {
            public Panel Root { get; set; }

            public Panel Body { get; set; }
        }
    }
}
