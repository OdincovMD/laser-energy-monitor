using System;
using System.IO;
using System.Windows.Forms;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;
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
            IOperatorNotifier notifier = new MessageBoxOperatorNotifier();
            IClock clock = new SystemClock();
            MeasurementSessionRuntimeFactory runtimeFactory = new MeasurementSessionRuntimeFactory(logPath, notifier, clock);

            System.Windows.Forms.Application.Run(new MainForm(runtimeFactory, outputDir));
        }
    }
}
