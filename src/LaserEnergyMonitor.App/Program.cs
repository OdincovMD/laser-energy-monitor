using System;
using System.IO;
using System.Windows.Forms;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;
using LaserEnergyMonitor.Infrastructure.Excel;
using LaserEnergyMonitor.Infrastructure.Logging;

namespace LaserEnergyMonitor.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            string logPath = Path.Combine(outputDir, "application.log");

            IMeasurementSource beamSource = new SimulatedMeasurementSource("BeamGage", 10.0d, 50);
            IMeasurementSource ophirSource = new SimulatedMeasurementSource("Ophir", 10.2d, 50);
            IMeasurementSynchronizer synchronizer = new TimeWindowMeasurementSynchronizer();
            IStationarityDetector detector = new RollingStationarityDetector();
            IMeasurementExporter exporter = new PrototypeExcelExporter();
            IApplicationLogger logger = new FileApplicationLogger(logPath);
            IOperatorNotifier notifier = new MessageBoxOperatorNotifier();
            IClock clock = new SystemClock();

            MeasurementSessionService service = new MeasurementSessionService(
                beamSource,
                ophirSource,
                synchronizer,
                detector,
                exporter,
                logger,
                notifier,
                clock);

            System.Windows.Forms.Application.Run(new MainForm(service, outputDir));
        }
    }
}
