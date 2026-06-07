namespace LaserEnergyMonitor.Application
{
    public static class OperatorSessionSettingsPolicy
    {
        public const int DefaultRollingWindowSize = 20;
        public const double DefaultEnterThresholdPercent = 0.5d;
        public const double DefaultExitThresholdPercent = 1.0d;
    }
}
