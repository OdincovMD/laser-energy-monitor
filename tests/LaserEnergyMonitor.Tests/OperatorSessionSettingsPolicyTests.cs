using LaserEnergyMonitor.Application;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class OperatorSessionSettingsPolicyTests
    {
        [Fact]
        public void OperatorDefaults_MatchWarmUpAcceptanceSettings()
        {
            Assert.Equal(20, OperatorSessionSettingsPolicy.DefaultRollingWindowSize);
            Assert.Equal(0.5d, OperatorSessionSettingsPolicy.DefaultEnterThresholdPercent);
            Assert.Equal(1.0d, OperatorSessionSettingsPolicy.DefaultExitThresholdPercent);
        }
    }
}
