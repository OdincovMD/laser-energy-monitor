using System.Windows;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Wpf
{
    public sealed class WpfOperatorNotifier : IOperatorNotifier
    {
        public void ShowInfo(string message)
        {
            MessageBox.Show(message, "Laser Energy Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message)
        {
            MessageBox.Show(message, "Laser Energy Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowCritical(string message)
        {
            MessageBox.Show(message, "Laser Energy Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
