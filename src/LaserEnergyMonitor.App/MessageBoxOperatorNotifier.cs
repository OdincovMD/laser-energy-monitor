using System.Windows.Forms;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.App
{
    public sealed class MessageBoxOperatorNotifier : IOperatorNotifier
    {
        public void ShowInfo(string message)
        {
            MessageBox.Show(message, "Laser Energy Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void ShowWarning(string message)
        {
            MessageBox.Show(message, "Laser Energy Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void ShowCritical(string message)
        {
            MessageBox.Show(message, "Laser Energy Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
