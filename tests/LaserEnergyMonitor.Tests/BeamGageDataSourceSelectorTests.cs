using System;
using LaserEnergyMonitor.Infrastructure.BeamGage;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class BeamGageDataSourceSelectorTests
    {
        [Fact]
        public void ResolvePhysicalDataSource_SkipsBuiltInSourcesDuringAutomaticSelection()
        {
            string selectedDataSource = BeamGageDataSourceSelector.ResolvePhysicalDataSource(
                new[] { "BeamMaker", "FileConsole", "SP928 #12345" },
                null);

            Assert.Equal("SP928 #12345", selectedDataSource);
        }

        [Fact]
        public void GetPhysicalDataSources_ReturnsOnlyPhysicalSensors()
        {
            string[] dataSources = BeamGageDataSourceSelector.GetPhysicalDataSources(
                new[] { "BeamMaker", "SP928 #12345", "FileConsole", "SP928 #67890" });

            Assert.Equal(new[] { "SP928 #12345", "SP928 #67890" }, dataSources);
        }

        [Fact]
        public void ResolvePhysicalDataSource_RejectsListWithoutPhysicalSource()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeamGageDataSourceSelector.ResolvePhysicalDataSource(
                    new[] { "BeamMaker", "FileConsole" },
                    null));

            Assert.Contains("no physical data sources", exception.Message);
        }

        [Fact]
        public void ResolvePhysicalDataSource_RejectsConfiguredBuiltInSource()
        {
            Assert.Throws<InvalidOperationException>(
                () => BeamGageDataSourceSelector.ResolvePhysicalDataSource(
                    new[] { "BeamMaker", "SP928 #12345" },
                    "BeamMaker"));
        }

        [Fact]
        public void EnsureActivePhysicalDataSource_RejectsUnexpectedSourceChange()
        {
            Assert.Throws<InvalidOperationException>(
                () => BeamGageDataSourceSelector.EnsureActivePhysicalDataSource(
                    "SP928 #12345",
                    "BeamMaker",
                    new[] { "BeamMaker", "SP928 #12345" }));
        }

        [Fact]
        public void EnsureActivePhysicalDataSource_RejectsDisconnectedSource()
        {
            Assert.Throws<InvalidOperationException>(
                () => BeamGageDataSourceSelector.EnsureActivePhysicalDataSource(
                    "SP928 #12345",
                    "SP928 #12345",
                    new[] { "BeamMaker" }));
        }

        [Fact]
        public void ShouldPublishFrame_RejectsDuplicateAndOlderFrameIds()
        {
            Assert.True(BeamGageDataSourceSelector.ShouldPublishFrame(10L, null));
            Assert.True(BeamGageDataSourceSelector.ShouldPublishFrame(11L, 10L));
            Assert.False(BeamGageDataSourceSelector.ShouldPublishFrame(10L, 10L));
            Assert.False(BeamGageDataSourceSelector.ShouldPublishFrame(9L, 10L));
        }
    }
}
