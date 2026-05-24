using System;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    public sealed class BeamGagePrerequisiteException : InvalidOperationException
    {
        public BeamGagePrerequisiteException(string message)
            : base(message)
        {
        }

        public BeamGagePrerequisiteException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
