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
        private readonly ToolTip _uiToolTip;
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
        private NumericUpDown _maxConsecutiveDesyncInput;
        private ComboBox _desynchronizationPolicyComboBox;
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
        private Label _stationaryEntriesLabel;
        private Label _statusBannerTitleLabel;
        private Label _statusBannerMessageLabel;
        private Label _summaryOutcomeLabel;
        private Label _summaryFinalStateLabel;
        private Label _summaryFinishedLabel;
        private Label _summaryPairsLabel;
        private Label _summaryEventsLabel;
        private Label _summarySegmentsLabel;
        private Label _summaryDesynchronizationsLabel;
        private Label _summaryFaultsLabel;
        private Label _summaryLastSignalLabel;
        private ListBox _stationarySegmentsListBox;
        private ListBox _eventsListBox;
        private Button _browseOutputButton;
        private Button _clearEventsButton;
        private Button _initializeButton;
        private Button _selfTestButton;
        private Button _beamGageSmokeTestButton;
        private Button _ophirSmokeTestButton;
        private Button _startButton;
        private Button _stopButton;
        private DeviceStatusView _beamStatusView;
        private DeviceStatusView _ophirStatusView;
        private int _stationaryEntryCount;

        public MainForm(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;
            _uiToolTip = new ToolTip();
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
            ResetSessionReview();
            RefreshSourceDiagnostics();
            AddEvent("Application ready. Configure sources or start diagnostics.");
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
            root.RowCount = 2;
            root.Padding = new Padding(24);
            root.BackColor = BackColor;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            root.Controls.Add(BuildHeaderPanel(), 0, 0);

            SplitContainer workspaceSplit = new SplitContainer();
            workspaceSplit.Dock = DockStyle.Fill;
            workspaceSplit.Margin = new Padding(0, 18, 0, 0);
            workspaceSplit.Orientation = Orientation.Horizontal;
            workspaceSplit.BackColor = BackColor;
            workspaceSplit.BorderStyle = BorderStyle.None;
            workspaceSplit.SplitterWidth = 8;
            workspaceSplit.FixedPanel = FixedPanel.None;
            root.Controls.Add(workspaceSplit, 0, 1);

            SplitContainer mainContentSplit = new SplitContainer();
            mainContentSplit.Dock = DockStyle.Fill;
            mainContentSplit.Orientation = Orientation.Vertical;
            mainContentSplit.BackColor = BackColor;
            mainContentSplit.BorderStyle = BorderStyle.None;
            mainContentSplit.SplitterWidth = 8;
            workspaceSplit.Panel1.Controls.Add(mainContentSplit);

            SectionCardView settingsCard = CreateSectionCard(
                "Session Control",
                "Configure the run, verify source health, and launch diagnostics from a single control surface.");
            settingsCard.Root.Margin = new Padding(0);
            settingsCard.Root.MinimumSize = new Size(480, 0);
            mainContentSplit.Panel1.Controls.Add(settingsCard.Root);

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
            settingsLayout.RowCount = 12;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 10; row++)
            {
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 340));
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
            _maxConsecutiveDesyncInput = CreateNumeric(3m, 0m, 1000m, 0);
            _desynchronizationPolicyComboBox = CreateDesynchronizationPolicyComboBox(DesynchronizationPolicyAction.FaultSession);
            _outputPathTextBox = new TextBox();
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
            ApplyInputStyle(_maxConsecutiveDesyncInput);
            ApplyInputStyle(_desynchronizationPolicyComboBox);
            ApplyInputStyle(_outputPathTextBox);
            ApplyReadOnlyTextStyle(_sourceDiagnosticsTextBox);
            UpdateOutputPathDisplay(Path.Combine(_defaultOutputDir, "measurement-session.xlsx"));
            ApplyAccessibilityMetadata();

            AddLabeledControl(settingsLayout, 0, "BeamGage Source", _firstSourceComboBox);
            AddLabeledControl(settingsLayout, 1, "Ophir Source", _secondSourceComboBox);
            AddLabeledControl(settingsLayout, 2, "Session Name", _sessionNameTextBox);
            AddLabeledControl(settingsLayout, 3, "Window N", _windowSizeInput);
            AddLabeledControl(settingsLayout, 4, "Enter Threshold %", _enterThresholdInput);
            AddLabeledControl(settingsLayout, 5, "Exit Threshold %", _exitThresholdInput);
            AddLabeledControl(settingsLayout, 6, "Sync Delta ms", _syncDeltaInput);
            AddLabeledControl(settingsLayout, 7, "Max Consecutive Desyncs", _maxConsecutiveDesyncInput);
            AddLabeledControl(settingsLayout, 8, "Desync Policy", _desynchronizationPolicyComboBox);
            AddLabeledControl(settingsLayout, 9, "Output Path", BuildOutputPathPanel());
            AddLabeledControl(settingsLayout, 10, "Source Status", BuildDiagnosticsTabs());

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
            _beamGageSmokeTestButton = new Button();
            _beamGageSmokeTestButton.Text = "BeamGage Smoke-Test";
            _ophirSmokeTestButton = new Button();
            _ophirSmokeTestButton.Text = "Ophir Smoke-Test";
            _startButton = new Button();
            _startButton.Text = "Start";
            _stopButton = new Button();
            _stopButton.Text = "Stop";

            ApplyPrimaryButtonStyle(_initializeButton, false);
            ApplyPrimaryButtonStyle(_selfTestButton, false);
            ApplyPrimaryButtonStyle(_beamGageSmokeTestButton, false);
            ApplyPrimaryButtonStyle(_ophirSmokeTestButton, false);
            ApplyPrimaryButtonStyle(_startButton, true);
            ApplyPrimaryButtonStyle(_stopButton, false);

            buttonsPanel.Controls.Add(_initializeButton);
            buttonsPanel.Controls.Add(_selfTestButton);
            buttonsPanel.Controls.Add(_beamGageSmokeTestButton);
            buttonsPanel.Controls.Add(_ophirSmokeTestButton);
            buttonsPanel.Controls.Add(_startButton);
            buttonsPanel.Controls.Add(_stopButton);
            AddLabeledControl(settingsLayout, 11, "Actions", buttonsPanel);

            SectionCardView liveCard = CreateSectionCard(
                "Live Status",
                "Keep the session state, pair timing, and energy metrics visible without fighting the layout.");
            liveCard.Root.Margin = new Padding(0);
            mainContentSplit.Panel2.Controls.Add(liveCard.Root);

            TableLayoutPanel liveLayout = new TableLayoutPanel();
            liveLayout.Dock = DockStyle.Fill;
            liveLayout.ColumnCount = 1;
            liveLayout.RowCount = 2;
            liveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
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
            _stationaryEntriesLabel = new Label();
            _summaryOutcomeLabel = new Label();
            _summaryFinalStateLabel = new Label();
            _summaryFinishedLabel = new Label();
            _summaryPairsLabel = new Label();
            _summaryEventsLabel = new Label();
            _summarySegmentsLabel = new Label();
            _summaryDesynchronizationsLabel = new Label();
            _summaryFaultsLabel = new Label();
            _summaryLastSignalLabel = new Label();
            _stationarySegmentsListBox = new ListBox();

            liveLayout.Controls.Add(BuildStateBanner(), 0, 0);
            liveLayout.Controls.Add(BuildMetricsGrid(), 0, 1);

            SplitContainer reviewSplit = new SplitContainer();
            reviewSplit.Dock = DockStyle.Fill;
            reviewSplit.Orientation = Orientation.Vertical;
            reviewSplit.BorderStyle = BorderStyle.None;
            reviewSplit.SplitterWidth = 8;
            workspaceSplit.Panel2.Controls.Add(reviewSplit);

            SectionCardView eventsCard = CreateSectionCard(
                "Events",
                "Recent operator and session activity stays visible at the bottom of the workspace.");
            eventsCard.Root.Margin = new Padding(0);
            eventsCard.Root.MinimumSize = new Size(0, 320);
            reviewSplit.Panel1.Controls.Add(eventsCard.Root);

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
            _eventsListBox.BorderStyle = BorderStyle.FixedSingle;
            _eventsListBox.BackColor = Color.FromArgb(249, 251, 253);
            _eventsListBox.ForeColor = Color.FromArgb(37, 45, 58);
            _eventsListBox.HorizontalScrollbar = true;
            _eventsListBox.IntegralHeight = false;
            _eventsListBox.Name = "EventsList";
            eventsLayout.Controls.Add(_eventsListBox, 0, 1);

            SectionCardView reviewCard = CreateSectionCard(
                "Session Review",
                "See the final series summary and every closed stationary segment without opening the export.");
            reviewCard.Root.Margin = new Padding(0);
            reviewCard.Root.MinimumSize = new Size(340, 320);
            reviewSplit.Panel2.Controls.Add(reviewCard.Root);

            TableLayoutPanel reviewLayout = new TableLayoutPanel();
            reviewLayout.Dock = DockStyle.Fill;
            reviewLayout.ColumnCount = 1;
            reviewLayout.RowCount = 2;
            reviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 244));
            reviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            reviewCard.Body.Controls.Add(reviewLayout);

            reviewLayout.Controls.Add(BuildSummaryGrid(), 0, 0);
            reviewLayout.Controls.Add(BuildStationarySegmentsPanel(), 0, 1);
            ApplyAccessibilityMetadata();

            Shown += delegate
            {
                if (mainContentSplit.Width > 0)
                {
                    int leftMinWidth = 480;
                    int rightMinWidth = 520;
                    int preferredLeftWidth = Math.Max(
                        leftMinWidth,
                        Math.Min(540, mainContentSplit.Width - rightMinWidth - mainContentSplit.SplitterWidth));
                    mainContentSplit.SplitterDistance = preferredLeftWidth;
                    mainContentSplit.Panel1MinSize = leftMinWidth;
                    mainContentSplit.Panel2MinSize = rightMinWidth;
                }

                if (workspaceSplit.Height > 0)
                {
                    int topMinHeight = 480;
                    int bottomMinHeight = 320;
                    int preferredBottomHeight = Math.Max(bottomMinHeight, 320);
                    int preferredTopHeight = Math.Max(
                        topMinHeight,
                        workspaceSplit.Height - preferredBottomHeight - workspaceSplit.SplitterWidth);
                    workspaceSplit.SplitterDistance = preferredTopHeight;
                    workspaceSplit.Panel1MinSize = topMinHeight;
                    workspaceSplit.Panel2MinSize = bottomMinHeight;
                }

                if (reviewSplit.Width > 0)
                {
                    int reviewPanelWidth = Math.Max(360, Math.Min(480, reviewSplit.Width / 3));
                    reviewSplit.SplitterDistance = reviewSplit.Width - reviewPanelWidth - reviewSplit.SplitterWidth;
                    reviewSplit.Panel1MinSize = 420;
                    reviewSplit.Panel2MinSize = 340;
                }
            };

            ResumeLayout(true);
        }

        private void WireEvents()
        {
            _initializeButton.Click += OnInitializeClicked;
            _selfTestButton.Click += OnSelfTestClicked;
            _beamGageSmokeTestButton.Click += OnBeamGageSmokeTestClicked;
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
                    UpdateOutputPathDisplay(dialog.FileName);
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

        private void OnBeamGageSmokeTestClicked(object sender, EventArgs e)
        {
            try
            {
                string report = _runtimeFactory.RunBeamGageSmokeTest(GetSelectedSourceKey(_firstSourceComboBox));
                _sourceDiagnosticsTextBox.Text = report;
                AddEvent("BeamGage smoke-test completed.");
                MessageBox.Show(report, "BeamGage Smoke-Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BeamGage smoke-test error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            UpdateInlineStatusForEvent(sessionEvent);
            if (sessionEvent.EventType == SessionEventType.StationaryEntered)
            {
                _stationaryEntryCount += 1;
                _stationaryEntriesLabel.Text = _stationaryEntryCount.ToString(CultureInfo.InvariantCulture);
            }

            AddEvent(string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss} [{1}] {2}",
                sessionEvent.TimestampUtc.ToLocalTime(),
                sessionEvent.EventType,
                sessionEvent.Message));
        }

        private void OnStationarySegmentRecorded(object sender, StationarySegmentRecordedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, StationarySegmentRecordedEventArgs>(OnStationarySegmentRecorded), sender, e);
                return;
            }

            StationarySegmentResult segment = e != null ? e.Segment : null;
            if (segment == null)
            {
                return;
            }

            _stationarySegmentsListBox.Items.Insert(0, FormatStationarySegment(segment));
            while (_stationarySegmentsListBox.Items.Count > 100)
            {
                _stationarySegmentsListBox.Items.RemoveAt(_stationarySegmentsListBox.Items.Count - 1);
            }
        }

        private void OnSessionSummaryAvailable(object sender, SessionSummaryAvailableEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, SessionSummaryAvailableEventArgs>(OnSessionSummaryAvailable), sender, e);
                return;
            }

            BindSessionSummary(e != null ? e.Summary : null);
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
                MaxConsecutiveDesynchronizations = Decimal.ToInt32(_maxConsecutiveDesyncInput.Value),
                DesynchronizationPolicyAction = GetSelectedDesynchronizationPolicyAction(_desynchronizationPolicyComboBox),
                OutputPath = _outputPathTextBox.Text
            };
        }

        private void UpdateState(MeasurementSessionState state)
        {
            _stateLabel.Text = state.ToString();
            _stateLabel.ForeColor = GetStateColor(state);
            _stateLabel.BackColor = GetStateBackColor(state);

            _startButton.Enabled = state == MeasurementSessionState.Idle || state == MeasurementSessionState.Initialized || state == MeasurementSessionState.Completed;
            _stopButton.Enabled = state == MeasurementSessionState.Measuring || state == MeasurementSessionState.Stationary;
            ApplySessionConfigurationState(state);
            if (state == MeasurementSessionState.Idle || state == MeasurementSessionState.Initialized)
            {
                ResetLiveValues();
            }

            UpdateInlineStatusForState(state);
        }

        private void AddEvent(string message)
        {
            _eventsListBox.Items.Insert(0, message);
            while (_eventsListBox.Items.Count > 500)
            {
                _eventsListBox.Items.RemoveAt(_eventsListBox.Items.Count - 1);
            }
        }

        private void UpdateOutputPathDisplay(string path)
        {
            _outputPathTextBox.Text = path;
            _outputPathTextBox.SelectionStart = _outputPathTextBox.TextLength;
            _outputPathTextBox.SelectionLength = 0;
            _outputPathTextBox.ScrollToCaret();
            _uiToolTip.SetToolTip(_outputPathTextBox, path);
        }

        private MeasurementSessionService EnsureInitializedService(SessionSettings settings, bool forceRecreate)
        {
            string firstKey = GetSelectedSourceKey(_firstSourceComboBox);
            string secondKey = GetSelectedSourceKey(_secondSourceComboBox);
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

        private void ReplaceService(MeasurementSessionService newService)
        {
            if (_service != null)
            {
                _service.StateChanged -= OnStateChanged;
                _service.LiveMeasurementUpdated -= OnLiveMeasurementUpdated;
                _service.SessionEventRaised -= OnSessionEventRaised;
                _service.StationarySegmentRecorded -= OnStationarySegmentRecorded;
                _service.SessionSummaryAvailable -= OnSessionSummaryAvailable;
                _service.Dispose();
            }

            _service = newService;

            if (_service != null)
            {
                _service.StateChanged += OnStateChanged;
                _service.LiveMeasurementUpdated += OnLiveMeasurementUpdated;
                _service.SessionEventRaised += OnSessionEventRaised;
                _service.StationarySegmentRecorded += OnStationarySegmentRecorded;
                _service.SessionSummaryAvailable += OnSessionSummaryAvailable;
            }
            else
            {
                ResetLiveValues();
                ResetSessionReview();
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
            tabs.MinimumSize = new Size(0, 340);

            TabPage overviewPage = new TabPage("Overview");
            overviewPage.Name = "OverviewTab";
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
            reportPage.Name = "ReportTab";
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
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
            acquisitionLabel.AutoEllipsis = true;
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
            _stationaryEntryCount = 0;
            _stationaryEntriesLabel.Text = "0";
        }

        private void ResetSessionReview()
        {
            _summaryOutcomeLabel.Text = "Waiting";
            _summaryFinalStateLabel.Text = "-";
            _summaryFinishedLabel.Text = "-";
            _summaryPairsLabel.Text = "0";
            _summaryEventsLabel.Text = "0";
            _summarySegmentsLabel.Text = "0";
            _summaryDesynchronizationsLabel.Text = "0";
            _summaryFaultsLabel.Text = "0";
            _summaryLastSignalLabel.Text = "-";
            _uiToolTip.SetToolTip(_summaryLastSignalLabel, string.Empty);
            _stationarySegmentsListBox.Items.Clear();
        }

        private void BindSessionSummary(SessionSummary summary)
        {
            if (summary == null)
            {
                ResetSessionReview();
                return;
            }

            _summaryOutcomeLabel.Text = summary.CompletedNormally ? "Completed" : "Aborted";
            _summaryFinalStateLabel.Text = string.IsNullOrWhiteSpace(summary.FinalState) ? "-" : summary.FinalState;
            _summaryFinishedLabel.Text = summary.FinishedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _summaryPairsLabel.Text = summary.PairCount.ToString(CultureInfo.InvariantCulture);
            _summaryEventsLabel.Text = summary.EventCount.ToString(CultureInfo.InvariantCulture);
            _summarySegmentsLabel.Text = summary.ClosedStationarySegmentCount.ToString(CultureInfo.InvariantCulture);
            _summaryDesynchronizationsLabel.Text = summary.DesynchronizationCount.ToString(CultureInfo.InvariantCulture);
            _summaryFaultsLabel.Text = summary.FaultCount.ToString(CultureInfo.InvariantCulture);
            _summaryLastSignalLabel.Text = BuildTerminationText(summary);
            _uiToolTip.SetToolTip(_summaryLastSignalLabel, BuildTerminationTooltip(summary));
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

        private static ComboBox CreateDesynchronizationPolicyComboBox(DesynchronizationPolicyAction defaultAction)
        {
            ComboBox comboBox = new ComboBox();
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;

            DesynchronizationPolicyOption[] options =
            {
                new DesynchronizationPolicyOption(DesynchronizationPolicyAction.LogOnly, "Log Only"),
                new DesynchronizationPolicyOption(DesynchronizationPolicyAction.StopGracefully, "Stop Gracefully"),
                new DesynchronizationPolicyOption(DesynchronizationPolicyAction.FaultSession, "Fault Session")
            };

            for (int i = 0; i < options.Length; i++)
            {
                comboBox.Items.Add(options[i]);
                if (options[i].Action == defaultAction)
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

        private static DesynchronizationPolicyAction GetSelectedDesynchronizationPolicyAction(ComboBox comboBox)
        {
            DesynchronizationPolicyOption option = comboBox != null ? comboBox.SelectedItem as DesynchronizationPolicyOption : null;
            return option != null ? option.Action : DesynchronizationPolicyAction.FaultSession;
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

        private void ApplySessionConfigurationState(MeasurementSessionState state)
        {
            bool locked = IsSessionConfigurationLocked(state);
            _initializeButton.Enabled = !locked;
            _selfTestButton.Enabled = !locked;
            _beamGageSmokeTestButton.Enabled = !locked;
            _ophirSmokeTestButton.Enabled = !locked;
            _firstSourceComboBox.Enabled = !locked;
            _secondSourceComboBox.Enabled = !locked;
            _sessionNameTextBox.ReadOnly = locked;
            _windowSizeInput.Enabled = !locked;
            _enterThresholdInput.Enabled = !locked;
            _exitThresholdInput.Enabled = !locked;
            _syncDeltaInput.Enabled = !locked;
            _maxConsecutiveDesyncInput.Enabled = !locked;
            _desynchronizationPolicyComboBox.Enabled = !locked;
            _outputPathTextBox.ReadOnly = locked;
            _browseOutputButton.Enabled = !locked;
        }

        private static bool IsSessionConfigurationLocked(MeasurementSessionState state)
        {
            return state == MeasurementSessionState.Measuring || state == MeasurementSessionState.Stationary;
        }

        private static string BuildLastSignalText(SessionSummary summary)
        {
            if (summary == null)
            {
                return "-";
            }

            if (summary.LastFaultUtc.HasValue)
            {
                return "Fault " + summary.LastFaultUtc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }

            if (summary.LastDesynchronizationUtc.HasValue)
            {
                return "Desync " + summary.LastDesynchronizationUtc.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return "None";
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

        private static string FormatStationarySegment(StationarySegmentResult segment)
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

        private void UpdateInlineStatusForState(MeasurementSessionState state)
        {
            switch (state)
            {
                case MeasurementSessionState.Initialized:
                    SetInlineStatus(
                        "Initialized",
                        "Sources are ready. Start the session when the operator is ready to collect pulses.",
                        Color.FromArgb(232, 240, 254),
                        Color.FromArgb(17, 90, 153),
                        Color.FromArgb(38, 62, 92));
                    break;
                case MeasurementSessionState.Measuring:
                    SetInlineStatus(
                        "Measuring",
                        "Samples are flowing. Watch the live energy metrics and wait for stationary entry.",
                        Color.FromArgb(225, 245, 231),
                        Color.FromArgb(16, 124, 16),
                        Color.FromArgb(32, 82, 42));
                    break;
                case MeasurementSessionState.Stationary:
                    SetInlineStatus(
                        "Stationary Detected",
                        "A stable segment is active. The session keeps observing for drift or another stable interval.",
                        Color.FromArgb(255, 244, 214),
                        Color.FromArgb(114, 76, 0),
                        Color.FromArgb(97, 71, 24));
                    break;
                case MeasurementSessionState.Faulted:
                    SetInlineStatus(
                        "Faulted",
                        "The session stopped because a critical device or pipeline fault was reported.",
                        Color.FromArgb(255, 228, 224),
                        Color.FromArgb(180, 35, 24),
                        Color.FromArgb(112, 35, 30));
                    break;
                case MeasurementSessionState.Completed:
                    SetInlineStatus(
                        "Completed",
                        "The current session finished. Review the final metrics and export artifacts before the next run.",
                        Color.FromArgb(233, 236, 240),
                        Color.FromArgb(72, 81, 93),
                        Color.FromArgb(72, 81, 93));
                    break;
                default:
                    SetInlineStatus(
                        "Ready",
                        "Initialize the selected sources to begin a new measurement session.",
                        Color.FromArgb(232, 240, 254),
                        Color.FromArgb(17, 90, 153),
                        Color.FromArgb(38, 62, 92));
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
                    SetInlineStatus("Stationary Entered", sessionEvent.Message, Color.FromArgb(255, 244, 214), Color.FromArgb(114, 76, 0), Color.FromArgb(97, 71, 24));
                    break;
                case SessionEventType.StationaryExited:
                    SetInlineStatus("Stationary Exited", sessionEvent.Message, Color.FromArgb(255, 239, 214), Color.FromArgb(157, 87, 0), Color.FromArgb(101, 71, 26));
                    break;
                case SessionEventType.Desynchronized:
                    SetInlineStatus("Desynchronization", sessionEvent.Message, Color.FromArgb(255, 244, 214), Color.FromArgb(157, 87, 0), Color.FromArgb(101, 71, 26));
                    break;
                case SessionEventType.Fault:
                    SetInlineStatus("Critical Fault", sessionEvent.Message, Color.FromArgb(255, 228, 224), Color.FromArgb(180, 35, 24), Color.FromArgb(112, 35, 30));
                    break;
                case SessionEventType.SessionStarted:
                    SetInlineStatus("Session Started", sessionEvent.Message, Color.FromArgb(225, 245, 231), Color.FromArgb(16, 124, 16), Color.FromArgb(32, 82, 42));
                    break;
                case SessionEventType.SessionStopped:
                    SetInlineStatus("Session Stopped", sessionEvent.Message, Color.FromArgb(233, 236, 240), Color.FromArgb(72, 81, 93), Color.FromArgb(72, 81, 93));
                    break;
            }
        }

        private void SetInlineStatus(string title, string message, Color background, Color titleColor, Color messageColor)
        {
            if (_statusBannerTitleLabel == null || _statusBannerMessageLabel == null)
            {
                return;
            }

            Control banner = _statusBannerTitleLabel.Parent != null ? _statusBannerTitleLabel.Parent.Parent : null;
            if (banner != null)
            {
                banner.BackColor = background;
            }

            _statusBannerTitleLabel.Text = title;
            _statusBannerTitleLabel.ForeColor = titleColor;
            _statusBannerMessageLabel.Text = message;
            _statusBannerMessageLabel.ForeColor = messageColor;
        }

        private void ApplyAccessibilityMetadata()
        {
            SetAccessibility(_sessionNameTextBox, "Session name", "Operator-defined name for the next measurement run.");
            SetAccessibility(_firstSourceComboBox, "BeamGage source selector", "Choose the source implementation for the BeamGage measurement path.");
            SetAccessibility(_secondSourceComboBox, "Ophir source selector", "Choose the source implementation for the Ophir measurement path.");
            SetAccessibility(_windowSizeInput, "Rolling window size", "Number of recent synchronized pairs used to compute rolling averages.");
            SetAccessibility(_enterThresholdInput, "Stationary enter threshold", "Percentage threshold required to enter stationary mode.");
            SetAccessibility(_exitThresholdInput, "Stationary exit threshold", "Percentage threshold that causes the session to leave stationary mode.");
            SetAccessibility(_syncDeltaInput, "Synchronization delta", "Maximum allowed time difference in milliseconds between paired samples.");
            SetAccessibility(_maxConsecutiveDesyncInput, "Maximum consecutive desynchronizations", "Zero disables automatic stop. Any larger number stops the session after that many consecutive desynchronizations.");
            SetAccessibility(_desynchronizationPolicyComboBox, "Desynchronization policy", "Choose whether repeated desynchronization only logs, stops gracefully, or faults the session.");
            SetAccessibility(_outputPathTextBox, "Output path", "Destination workbook path for the session export.");
            SetAccessibility(_sourceDiagnosticsTextBox, "Source diagnostics report", "Read-only report showing current runtime checks for both measurement sources.");
            SetAccessibility(_eventsListBox, "Session event log", "Chronological log of session state changes, diagnostics, and faults.");
            SetAccessibility(_initializeButton, "Initialize sources", "Initialize the currently selected measurement sources.");
            SetAccessibility(_selfTestButton, "Run hardware self-test", "Run runtime checks for the selected source configuration.");
            SetAccessibility(_beamGageSmokeTestButton, "Run BeamGage smoke test", "Try a short live BeamGage acquisition through the configured SDK.");
            SetAccessibility(_ophirSmokeTestButton, "Run Ophir smoke test", "Try a short live Ophir acquisition through the configured SDK.");
            SetAccessibility(_startButton, "Start measurement session", "Start collecting synchronized measurements.");
            SetAccessibility(_stopButton, "Stop measurement session", "Stop the current measurement session.");
            SetAccessibility(_browseOutputButton, "Browse export path", "Choose the destination workbook path.");
            SetAccessibility(_clearEventsButton, "Clear event log", "Remove all rows from the event log view.");
            SetAccessibility(_stationarySegmentsListBox, "Stationary segment list", "Closed stationary segments recorded during the current session.");
        }

        private static void SetAccessibility(Control control, string accessibleName, string accessibleDescription)
        {
            if (control == null)
            {
                return;
            }

            control.AccessibleName = accessibleName;
            control.AccessibleDescription = accessibleDescription;
        }

        private Panel BuildHeaderPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(20, 34, 54);
            panel.Padding = new Padding(24, 20, 24, 18);
            panel.Margin = new Padding(0);
            panel.MinimumSize = new Size(0, 112);

            Label titleLabel = new Label();
            titleLabel.Text = "Laser Energy Monitor";
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 34;
            titleLabel.Font = new Font("Segoe UI Semibold", 19.0f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.White;

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "Configure sources, run hardware diagnostics, and monitor the live session state from one screen.";
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.AutoEllipsis = true;
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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            banner.Controls.Add(layout);

            layout.Controls.Add(_stateLabel, 0, 0);
            _stateLabel.Dock = DockStyle.Fill;

            TableLayoutPanel textLayout = new TableLayoutPanel();
            textLayout.Dock = DockStyle.Fill;
            textLayout.ColumnCount = 1;
            textLayout.RowCount = 3;
            textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
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
            descriptionLabel.AutoEllipsis = true;
            descriptionLabel.ForeColor = Color.FromArgb(92, 104, 120);

            textLayout.Controls.Add(titleLabel, 0, 0);
            textLayout.Controls.Add(BuildInlineStatusBanner(), 0, 1);
            textLayout.Controls.Add(descriptionLabel, 0, 2);
            return banner;
        }

        private Control BuildMetricsGrid()
        {
            TableLayoutPanel metricsLayout = new TableLayoutPanel();
            metricsLayout.Dock = DockStyle.Fill;
            metricsLayout.ColumnCount = 3;
            metricsLayout.RowCount = 3;
            metricsLayout.Padding = new Padding(0, 16, 0, 0);
            metricsLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));

            AddMetricCard(metricsLayout, "Pair ID", _pairIdLabel, 0, 0);
            AddMetricCard(metricsLayout, "Last Update", _lastUpdatedLabel, 1, 0);
            AddMetricCard(metricsLayout, "Stationary", _stationaryLabel, 2, 0);
            AddMetricCard(metricsLayout, "Beam Gage Energy", _firstEnergyLabel, 0, 1);
            AddMetricCard(metricsLayout, "Ophir Energy", _secondEnergyLabel, 1, 1);
            AddMetricCard(metricsLayout, "Stability Metric", _metricLabel, 2, 1);
            AddMetricCard(metricsLayout, "Beam Gage Average", _firstAverageLabel, 0, 2);
            AddMetricCard(metricsLayout, "Ophir Average", _secondAverageLabel, 1, 2);
            AddMetricCard(metricsLayout, "Stationary Entries", _stationaryEntriesLabel, 2, 2);
            return metricsLayout;
        }

        private static void AddMetricCard(TableLayoutPanel layout, string title, Label valueLabel, int column, int row)
        {
            Control card = CreateMetricCard(title, valueLabel);
            int rightMargin = column < layout.ColumnCount - 1 ? 12 : 0;
            int bottomMargin = row < layout.RowCount - 1 ? 12 : 0;
            card.Margin = new Padding(0, 0, rightMargin, bottomMargin);
            layout.Controls.Add(card, column, row);
        }

        private Panel BuildInlineStatusBanner()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(12, 8, 12, 8);
            panel.BackColor = Color.FromArgb(232, 240, 254);
            panel.BorderStyle = BorderStyle.FixedSingle;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(layout);

            _statusBannerTitleLabel = new Label();
            _statusBannerTitleLabel.Dock = DockStyle.Fill;
            _statusBannerTitleLabel.Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
            _statusBannerTitleLabel.ForeColor = Color.FromArgb(17, 90, 153);
            _statusBannerTitleLabel.Text = "Ready";

            _statusBannerMessageLabel = new Label();
            _statusBannerMessageLabel.Dock = DockStyle.Fill;
            _statusBannerMessageLabel.ForeColor = Color.FromArgb(38, 62, 92);
            _statusBannerMessageLabel.Text = "Initialize the selected sources to begin a new measurement session.";

            layout.Controls.Add(_statusBannerTitleLabel, 0, 0);
            layout.Controls.Add(_statusBannerMessageLabel, 0, 1);
            return panel;
        }

        private Control BuildSummaryGrid()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 3;
            layout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));

            AddMetricCard(layout, "Outcome", _summaryOutcomeLabel, 0, 0);
            AddMetricCard(layout, "Final State", _summaryFinalStateLabel, 1, 0);
            AddMetricCard(layout, "Finished", _summaryFinishedLabel, 2, 0);
            AddMetricCard(layout, "Pairs", _summaryPairsLabel, 0, 1);
            AddMetricCard(layout, "Events", _summaryEventsLabel, 1, 1);
            AddMetricCard(layout, "Closed Segments", _summarySegmentsLabel, 2, 1);
            AddMetricCard(layout, "Desync Count", _summaryDesynchronizationsLabel, 0, 2);
            AddMetricCard(layout, "Fault Count", _summaryFaultsLabel, 1, 2);
            AddMetricCard(layout, "Termination", _summaryLastSignalLabel, 2, 2);
            return layout;
        }

        private Control BuildStationarySegmentsPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;

            Label titleLabel = new Label();
            titleLabel.Text = "Stationary Segments";
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 24;
            titleLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(36, 46, 58);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = "Each row captures the stable interval, duration, and exit reason.";
            subtitleLabel.Dock = DockStyle.Top;
            subtitleLabel.Height = 24;
            subtitleLabel.ForeColor = Color.FromArgb(92, 104, 120);

            _stationarySegmentsListBox.Dock = DockStyle.Fill;
            _stationarySegmentsListBox.BorderStyle = BorderStyle.FixedSingle;
            _stationarySegmentsListBox.BackColor = Color.FromArgb(249, 251, 253);
            _stationarySegmentsListBox.ForeColor = Color.FromArgb(37, 45, 58);
            _stationarySegmentsListBox.Font = new Font("Consolas", 8.75f, FontStyle.Regular, GraphicsUnit.Point);
            _stationarySegmentsListBox.HorizontalScrollbar = true;
            _stationarySegmentsListBox.IntegralHeight = false;

            panel.Controls.Add(_stationarySegmentsListBox);
            panel.Controls.Add(subtitleLabel);
            panel.Controls.Add(titleLabel);
            return panel;
        }

        private static Control CreateMetricCard(string title, Label valueLabel)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16, 14, 16, 14);
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
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

        private sealed class DesynchronizationPolicyOption
        {
            public DesynchronizationPolicyOption(DesynchronizationPolicyAction action, string displayName)
            {
                Action = action;
                DisplayName = displayName;
            }

            public DesynchronizationPolicyAction Action { get; private set; }

            public string DisplayName { get; private set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
