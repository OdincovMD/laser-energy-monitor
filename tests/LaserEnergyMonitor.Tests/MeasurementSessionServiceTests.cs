using System;
using System.Collections.Generic;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class MeasurementSessionServiceTests
    {
        [Fact]
        public void MeasurementSessionService_CompletesSummaryAndRecordsClosedStationarySegment()
        {
            ServiceHarness harness = CreateHarness();
            using (MeasurementSessionService service = harness.Service)
            {
                List<StationarySegmentResult> recordedSegments = new List<StationarySegmentResult>();
                SessionSummary summary = null;
                service.StationarySegmentRecorded += delegate(object sender, StationarySegmentRecordedEventArgs args)
                {
                    recordedSegments.Add(args.Segment);
                };
                service.SessionSummaryAvailable += delegate(object sender, SessionSummaryAvailableEventArgs args)
                {
                    summary = args.Summary;
                };

                service.Initialize(CreateSettings());
                service.Start();

                EmitSamples(harness, 1L, 100d, 200d, 0);
                EmitSamples(harness, 2L, 100d, 200d, 10);
                EmitSamples(harness, 3L, 100d, 200d, 20);
                Assert.Equal(MeasurementSessionState.Stationary, service.State);

                EmitSamples(harness, 4L, 120d, 240d, 30);
                Assert.Equal(MeasurementSessionState.Measuring, service.State);

                harness.Clock.UtcNow = harness.StartTimeUtc.AddSeconds(5);
                service.Stop();

                Assert.Equal(MeasurementSessionState.Completed, service.State);
                Assert.NotNull(summary);
                Assert.True(summary.CompletedNormally);
                Assert.Equal("Completed", summary.FinalState);
                Assert.Equal(8, summary.SampleCount);
                Assert.Equal(4, summary.EventCount);
                Assert.Equal(1, summary.StationarySegmentCount);
                Assert.Equal(1, summary.ClosedStationarySegmentCount);
                Assert.Equal(0, summary.FaultCount);
                Assert.Equal("manual-stop", summary.TerminationReasonCode);

                Assert.Single(recordedSegments);
                Assert.Single(harness.Exporter.StationarySegments);
                Assert.Equal(6L, recordedSegments[0].EntryRecordId);
                Assert.Equal(7L, recordedSegments[0].ExitRecordId);
                Assert.Equal("Stationary mode lost.", recordedSegments[0].ExitReason);

                Assert.Collection(
                    harness.Exporter.Events,
                    item => Assert.Equal(SessionEventType.SessionStarted, item.EventType),
                    item => Assert.Equal(SessionEventType.StationaryEntered, item.EventType),
                    item => Assert.Equal(SessionEventType.StationaryExited, item.EventType),
                    item => Assert.Equal(SessionEventType.SessionStopped, item.EventType));
            }
        }

        [Fact]
        public void MeasurementSessionService_ProcessesSamplesIndependentlyWithoutPairing()
        {
            ServiceHarness harness = CreateHarness();
            using (MeasurementSessionService service = harness.Service)
            {
                SessionSummary summary = null;
                service.SessionSummaryAvailable += delegate(object sender, SessionSummaryAvailableEventArgs args)
                {
                    summary = args.Summary;
                };

                service.Initialize(CreateSettings());
                service.Start();

                harness.FirstSource.EmitMeasurement(1L, harness.StartTimeUtc, 10d);
                harness.Clock.UtcNow = harness.StartTimeUtc.AddMilliseconds(50);
                harness.SecondSource.EmitMeasurement(1L, harness.StartTimeUtc.AddMilliseconds(50), 20d);

                harness.Clock.UtcNow = harness.StartTimeUtc.AddSeconds(1);
                service.Stop();

                Assert.NotNull(summary);
                Assert.True(summary.CompletedNormally);
                Assert.Equal(2, summary.SampleCount);
                Assert.Equal(2, summary.EventCount);
                Assert.Equal(2, harness.Exporter.Measurements.Count);
                Assert.Equal("BeamGage", harness.Exporter.Measurements[0].Sample.SourceId);
                Assert.Equal("Ophir", harness.Exporter.Measurements[1].Sample.SourceId);
                Assert.Equal("manual-stop", summary.TerminationReasonCode);
            }
        }

        [Fact]
        public void MeasurementSessionService_FaultAbortsSessionAndNotifiesOperator()
        {
            ServiceHarness harness = CreateHarness();
            using (MeasurementSessionService service = harness.Service)
            {
                SessionSummary summary = null;
                service.SessionSummaryAvailable += delegate(object sender, SessionSummaryAvailableEventArgs args)
                {
                    summary = args.Summary;
                };

                service.Initialize(CreateSettings());
                service.Start();

                harness.Clock.UtcNow = harness.StartTimeUtc.AddMilliseconds(250);
                harness.FirstSource.EmitFault("USB link lost.");

                Assert.Equal(MeasurementSessionState.Faulted, service.State);
                Assert.NotNull(summary);
                Assert.False(summary.CompletedNormally);
                Assert.Equal("Faulted", summary.FinalState);
                Assert.Equal(1, summary.FaultCount);
                Assert.Equal(2, summary.EventCount);
                Assert.Equal("critical-fault", summary.TerminationReasonCode);
                Assert.NotNull(harness.Exporter.AbortedSummary);
                Assert.Contains("USB link lost", harness.Exporter.AbortReason);
                Assert.Single(harness.Notifier.Criticals);
                Assert.False(harness.FirstSource.IsStarted);
                Assert.False(harness.SecondSource.IsStarted);
            }
        }

        [Fact]
        public void MeasurementSessionService_AcceptsMeasurementsEmittedDuringSourceStart()
        {
            ServiceHarness harness = CreateHarness();
            using (MeasurementSessionService service = harness.Service)
            {
                harness.FirstSource.OnStart = source => source.EmitMeasurement(1L, harness.StartTimeUtc, 10d);
                harness.SecondSource.OnStart = source => source.EmitMeasurement(1L, harness.StartTimeUtc.AddMilliseconds(2), 20d);

                service.Initialize(CreateSettings());
                service.Start();

                Assert.Equal(MeasurementSessionState.Measuring, service.State);
                Assert.Equal(2, harness.Exporter.Measurements.Count);
                Assert.Equal(1L, harness.Exporter.Measurements[0].RecordId);
                Assert.Equal(2L, harness.Exporter.Measurements[1].RecordId);
                Assert.Equal(10d, harness.Exporter.Measurements[0].Sample.Energy);
                Assert.Equal(20d, harness.Exporter.Measurements[1].Sample.Energy);
            }
        }

        [Fact]
        public void MeasurementSessionService_DisposeDuringActiveRun_AbortsSessionWithDisposedSummary()
        {
            ServiceHarness harness = CreateHarness();
            MeasurementSessionService service = harness.Service;
            SessionSummary summary = null;
            service.SessionSummaryAvailable += delegate(object sender, SessionSummaryAvailableEventArgs args)
            {
                summary = args.Summary;
            };

            service.Initialize(CreateSettings());
            service.Start();

            harness.Clock.UtcNow = harness.StartTimeUtc.AddMilliseconds(500);
            service.Dispose();

            Assert.NotNull(summary);
            Assert.False(summary.CompletedNormally);
            Assert.Equal("Disposed", summary.FinalState);
            Assert.Equal(1, summary.EventCount);
            Assert.Equal("service-disposed", summary.TerminationReasonCode);
            Assert.NotNull(harness.Exporter.AbortedSummary);
            Assert.Contains("disposed", harness.Exporter.AbortReason, StringComparison.OrdinalIgnoreCase);
            Assert.True(harness.Exporter.IsDisposed);
            Assert.True(harness.FirstSource.IsDisposed);
            Assert.True(harness.SecondSource.IsDisposed);
        }

        private static ServiceHarness CreateHarness()
        {
            DateTime startTimeUtc = new DateTime(2026, 5, 24, 10, 0, 0, DateTimeKind.Utc);
            AdjustableClock clock = new AdjustableClock(startTimeUtc);
            ManualMeasurementSource firstSource = new ManualMeasurementSource("BeamGage", true);
            ManualMeasurementSource secondSource = new ManualMeasurementSource("Ophir", true);
            RecordingMeasurementExporter exporter = new RecordingMeasurementExporter();
            RecordingLogger logger = new RecordingLogger();
            RecordingNotifier notifier = new RecordingNotifier();

            return new ServiceHarness
            {
                StartTimeUtc = startTimeUtc,
                Clock = clock,
                FirstSource = firstSource,
                SecondSource = secondSource,
                Exporter = exporter,
                Notifier = notifier,
                Service = new MeasurementSessionService(
                    firstSource,
                    secondSource,
                    new RollingStationarityDetector(),
                    exporter,
                    logger,
                    notifier,
                    clock)
            };
        }

        private static SessionSettings CreateSettings()
        {
            return new SessionSettings
            {
                SessionName = "Test Session",
                RollingWindowSize = 2,
                EnterThresholdPercent = 1d,
                ExitThresholdPercent = 5d,
                OutputPath = "artifacts\\test-session.xlsx"
            };
        }

        private static void EmitSamples(ServiceHarness harness, long sequenceNumber, double firstEnergy, double secondEnergy, int offsetMs)
        {
            DateTime timestamp = harness.StartTimeUtc.AddMilliseconds(offsetMs);
            harness.Clock.UtcNow = timestamp;
            harness.FirstSource.EmitMeasurement(sequenceNumber, timestamp, firstEnergy);
            harness.SecondSource.EmitMeasurement(sequenceNumber, timestamp.AddMilliseconds(2), secondEnergy);
        }

        private sealed class ServiceHarness
        {
            public DateTime StartTimeUtc { get; set; }

            public AdjustableClock Clock { get; set; }

            public ManualMeasurementSource FirstSource { get; set; }

            public ManualMeasurementSource SecondSource { get; set; }

            public RecordingMeasurementExporter Exporter { get; set; }

            public RecordingNotifier Notifier { get; set; }

            public MeasurementSessionService Service { get; set; }
        }
    }
}
