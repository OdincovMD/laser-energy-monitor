# Ophir Setup Notes

The Ophir integration currently performs a prerequisite probe before any live acquisition starts.

## Required machine state

- The Ophir vendor automation package must be installed locally.
- The COM ProgID `OphirLMMeasurement.CoLMMeasurement` must be registered.
- The installed vendor runtime must match the application's `x86` target.

## What the current adapter does

- `Initialize()` checks whether `OphirLMMeasurement.CoLMMeasurement` is registered.
- If the ProgID is missing, the app raises a clear prerequisite error instead of a generic COM activation failure.
- If the ProgID exists, the app can create the COM object, probe USB visibility, and run a short smoke-test against the real SDK source.
- If the device is visible, the app can attempt short live acquisition and optionally capture raw samples into CSV for later replay.

## App bootstrap mode

The WPF app reads per-source settings from `src/LaserEnergyMonitor.Wpf/App.config`.

- `MeasurementSources.BeamGageMode=simulation|hardware`
- `MeasurementSources.OphirMode=simulation|hardware`

Relevant settings now include:

- `MeasurementSources.OphirSerialNumber`
- `MeasurementSources.OphirPreferredChannel`
- `MeasurementSources.OphirPollIntervalMs`
- `MeasurementSources.OphirTimestampStrategy`
- `MeasurementSources.OphirCaptureDirectory`
- `MeasurementSources.OphirReplayPath`
- `MeasurementSources.OphirReplaySpeedMultiplier`
- `MeasurementSources.OphirSmokeTestDurationMs`

See [ophir-integration-checklist.md](C:/Users/hardb/laser-energy-monitor/docs/ophir-integration-checklist.md) before the first customer-side live test.
