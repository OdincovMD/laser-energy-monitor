using System;
using System.IO;
using System.Windows;
using LaserEnergyMonitor.App;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;

namespace LaserEnergyMonitor.Wpf
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            string logPath = Path.Combine(outputDir, "application.log");
            IOperatorNotifier notifier = new WpfOperatorNotifier();
            IClock clock = new SystemClock();
            MeasurementSessionRuntimeFactory runtimeFactory = new MeasurementSessionRuntimeFactory(logPath, notifier, clock);

            MainWindow = new MainWindow(runtimeFactory, outputDir);
            MainWindow.Show();
        }
    }
}
