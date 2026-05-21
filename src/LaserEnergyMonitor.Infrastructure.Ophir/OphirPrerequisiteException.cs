using System;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirPrerequisiteException : InvalidOperationException
    {
        public OphirPrerequisiteException(string message)
            : base(message)
        {
        }

        public OphirPrerequisiteException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
