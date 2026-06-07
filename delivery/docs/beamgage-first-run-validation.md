# BeamGage: first-run validation

## Purpose

This checklist finalizes the BeamGage side before customer testing.
It documents what is already confirmed locally and what must be verified
on the customer machine with real BeamGage sensors.

## Vendor documentation checked

The local BeamGage vendor documentation under `Beam Gage` was checked.
The relevant automation API is:

- `Beam Gage\Automation\Documentation\interfaceSpiricon_1_1Automation_1_1IADataSource.html`
- `Beam Gage\Automation\Examples\C#\BGCSharpExample\BGCSharpExample\Program.cs`

The important points for our implementation:

- `IADataSource.DataSourceList` returns available data sources.
- `IADataSource.DataSource` gets or sets the current data source by string.
- Vendor docs state that sources other than `BeamMaker` and `FileConsole`
  use the `name #serialNumber` format.
- `IADataSource.Status` reports the current data-source status.
- `IADataSource.Start()` and `IADataSource.Stop()` control acquisition.
- The vendor C# example subscribes to
  `AutomationFrameEvents(ResultsPriorityFrame).OnNewFrame`; it also warns
  that work inside the frame callback should be minimized.

This matches the application flow:

- scan reads `DataSourceList`;
- the UI shows selectable BeamGage sources;
- connect writes the selected string to `DataSource`;
- start/stop calls vendor `Start()` / `Stop()`;
- the frame callback is kept lightweight, with polling fallback used for
  data extraction when enabled.

## Current local status

On a machine without physical BeamGage sensors:

- BeamGage automation starts.
- Built-in sources such as `BeamMaker` and `File` / `FileConsole` are visible.
- Built-in sources can be selected and consumed.
- This confirms that the previous failure mode where no BeamGage sources were
  visible is addressed for the local no-sensor environment.

## Expected customer behavior

The customer has two physical BeamGage sensors.

Expected result after selecting `BeamGage SDK` and pressing `Scan`:

- the BeamGage source dropdown should contain built-in sources if exposed by
  BeamGage, plus two physical sensor entries;
- physical entries should follow the vendor `name #serialNumber` format;
- the operator should be able to select either physical sensor;
- pressing `Connect` should set that sensor as the active BeamGage data source;
- `BeamGage Test` should report the selected/current data source, status,
  callback/poll counters, and whether samples were received.

If the app is configured with no explicit data source, automatic selection
prefers a physical source when one is available. Built-in sources are allowed
for local validation and fallback diagnostics.

## Customer test procedure

1. Close other software that may hold exclusive access to the BeamGage sensor.
2. Start `LaserEnergyMonitor.Wpf.exe`.
3. In `Run Setup`, select `BeamGage SDK`.
4. Press `Scan`.
5. Confirm that the source dropdown shows both real sensors.
6. Select the first real sensor and press `Connect`.
7. Run `BeamGage Test`.
8. Save the generated `beamgage-smoke-test-*.txt` report.
9. Repeat steps 6-8 for the second real sensor.
10. Run a short measurement session with the selected production sensor.

## Pass criteria

BeamGage is considered ready for the customer stand when:

- both real sensors are visible in the app;
- each real sensor can be selected and connected;
- `BeamGage Test` receives live samples from at least one production sensor;
- no critical BeamGage fault is reported during a short run;
- output files are produced under `output`.

## Evidence to collect

Ask the customer to return:

- full text from the `BeamGage Test` dialog;
- generated `beamgage-smoke-test-*.txt` files;
- `application.log`;
- screenshots of the source dropdown if one or both physical sensors are not
  visible;
- BeamGage Professional version and connected sensor model/serial numbers.

## Known remaining risk

The local machine does not have physical BeamGage sensors, so the final proof
must happen on the customer stand. The current implementation is aligned with
the vendor `IADataSource` API and has been validated against built-in sources,
but physical sensor enumeration and streaming still depend on the customer's
BeamGage installation, licensing, USB visibility, and sensor availability.
