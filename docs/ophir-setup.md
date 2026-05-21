# Ophir Setup Notes

The Ophir integration currently performs a prerequisite probe before any live acquisition starts.

## Required machine state

- The Ophir vendor automation package must be installed locally.
- The COM ProgID `OphirLMMeasurement.CoLMMeasurement` must be registered.
- The installed vendor runtime must match the application's `x86` target.

## What the current adapter does

- `Initialize()` checks whether `OphirLMMeasurement.CoLMMeasurement` is registered.
- If the ProgID is missing, the app raises a clear prerequisite error instead of a generic COM activation failure.
- If the ProgID exists, the app can create the COM object, but streaming measurements are still not implemented in the repository yet.

## App bootstrap mode

The WinForms app reads per-source settings from `src/LaserEnergyMonitor.App/App.config`.

- `MeasurementSources.BeamGageMode=simulation|hardware`
- `MeasurementSources.OphirMode=simulation|hardware`

Set `MeasurementSources.OphirMode=hardware` when you want to probe only the real Ophir adapter. If the vendor runtime is missing, the app now reports that prerequisite explicitly.
