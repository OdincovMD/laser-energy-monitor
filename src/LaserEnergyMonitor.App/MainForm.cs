using System;
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
        private Label _stateLabel;
        private Label _firstEnergyLabel;
        private Label _secondEnergyLabel;
        private Label _firstAverageLabel;
        private Label _secondAverageLabel;
        private Label _metricLabel;
        private Label _stationaryLabel;
        private ListBox _eventsListBox;
        private Button _initializeButton;
        private Button _startButton;
        private Button _stopButton;

        public MainForm(MeasurementSessionRuntimeFactory runtimeFactory, string defaultOutputDir)
        {
            _runtimeFactory = runtimeFactory;
            _defaultOutputDir = defaultOutputDir;
            Text = "Laser Energy Monitor Prototype";
            Width = 980;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;

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
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            GroupBox settingsGroup = new GroupBox();
            settingsGroup.Text = "Session Settings";
            settingsGroup.Dock = DockStyle.Fill;
            root.Controls.Add(settingsGroup, 0, 0);

            TableLayoutPanel settingsLayout = new TableLayoutPanel();
            settingsLayout.Dock = DockStyle.Fill;
            settingsLayout.ColumnCount = 2;
            settingsLayout.RowCount = 10;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
            _sourceDiagnosticsTextBox = new TextBox();
            _sourceDiagnosticsTextBox.Multiline = true;
            _sourceDiagnosticsTextBox.ReadOnly = true;
            _sourceDiagnosticsTextBox.ScrollBars = ScrollBars.Vertical;
            _sourceDiagnosticsTextBox.Height = 96;

            AddLabeledControl(settingsLayout, 0, "BeamGage Source", _firstSourceComboBox);
            AddLabeledControl(settingsLayout, 1, "Ophir Source", _secondSourceComboBox);
            AddLabeledControl(settingsLayout, 2, "Session Name", _sessionNameTextBox);
            AddLabeledControl(settingsLayout, 3, "Window N", _windowSizeInput);
            AddLabeledControl(settingsLayout, 4, "Enter Threshold %", _enterThresholdInput);
            AddLabeledControl(settingsLayout, 5, "Exit Threshold %", _exitThresholdInput);
            AddLabeledControl(settingsLayout, 6, "Sync Delta ms", _syncDeltaInput);
            AddLabeledControl(settingsLayout, 7, "Output Path", _outputPathTextBox);
            AddLabeledControl(settingsLayout, 8, "Source Status", _sourceDiagnosticsTextBox);

            FlowLayoutPanel buttonsPanel = new FlowLayoutPanel();
            buttonsPanel.Dock = DockStyle.Fill;
            _initializeButton = new Button();
            _initializeButton.Text = "Initialize";
            _startButton = new Button();
            _startButton.Text = "Start";
            _stopButton = new Button();
            _stopButton.Text = "Stop";
            buttonsPanel.Controls.Add(_initializeButton);
            buttonsPanel.Controls.Add(_startButton);
            buttonsPanel.Controls.Add(_stopButton);
            AddLabeledControl(settingsLayout, 9, "Actions", buttonsPanel);

            GroupBox liveGroup = new GroupBox();
            liveGroup.Text = "Live Status";
            liveGroup.Dock = DockStyle.Fill;
            root.Controls.Add(liveGroup, 1, 0);

            TableLayoutPanel liveLayout = new TableLayoutPanel();
            liveLayout.Dock = DockStyle.Fill;
            liveLayout.ColumnCount = 2;
            liveLayout.RowCount = 7;
            liveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            liveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            liveGroup.Controls.Add(liveLayout);

            _stateLabel = new Label();
            _firstEnergyLabel = new Label();
            _secondEnergyLabel = new Label();
            _firstAverageLabel = new Label();
            _secondAverageLabel = new Label();
            _metricLabel = new Label();
            _stationaryLabel = new Label();

            AddLabeledValue(liveLayout, 0, "State", _stateLabel);
            AddLabeledValue(liveLayout, 1, "Beam Gage Energy", _firstEnergyLabel);
            AddLabeledValue(liveLayout, 2, "Ophir Energy", _secondEnergyLabel);
            AddLabeledValue(liveLayout, 3, "Beam Gage Average", _firstAverageLabel);
            AddLabeledValue(liveLayout, 4, "Ophir Average", _secondAverageLabel);
            AddLabeledValue(liveLayout, 5, "Stability Metric", _metricLabel);
            AddLabeledValue(liveLayout, 6, "Stationary", _stationaryLabel);

            GroupBox eventsGroup = new GroupBox();
            eventsGroup.Text = "Events";
            eventsGroup.Dock = DockStyle.Fill;
            root.SetColumnSpan(eventsGroup, 2);
            root.Controls.Add(eventsGroup, 0, 1);

            _eventsListBox = new ListBox();
            _eventsListBox.Dock = DockStyle.Fill;
            eventsGroup.Controls.Add(_eventsListBox);
        }

        private void WireEvents()
        {
            _initializeButton.Click += OnInitializeClicked;
            _startButton.Click += OnStartClicked;
            _stopButton.Click += OnStopClicked;
            _firstSourceComboBox.SelectedIndexChanged += OnSourceSelectionChanged;
            _secondSourceComboBox.SelectedIndexChanged += OnSourceSelectionChanged;
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
            _startButton.Enabled = state == MeasurementSessionState.Idle || state == MeasurementSessionState.Initialized || state == MeasurementSessionState.Completed;
            _stopButton.Enabled = state == MeasurementSessionState.Measuring || state == MeasurementSessionState.Stationary || state == MeasurementSessionState.Faulted;
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
        }

        private void RefreshSourceDiagnostics()
        {
            if (_sourceDiagnosticsTextBox == null)
            {
                return;
            }

            _sourceDiagnosticsTextBox.Text = _runtimeFactory.BuildDiagnostics(
                GetSelectedSourceKey(_firstSourceComboBox),
                GetSelectedSourceKey(_secondSourceComboBox));
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
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private static void AddLabeledValue(TableLayoutPanel layout, int row, string labelText, Label valueLabel)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            valueLabel.AutoSize = true;
            valueLabel.Anchor = AnchorStyles.Left;
            valueLabel.Text = "-";
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(valueLabel, 1, row);
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "-";
        }
    }
}
