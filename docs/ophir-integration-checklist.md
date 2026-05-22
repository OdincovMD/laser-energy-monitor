# Ophir Integration Checklist

Use this checklist before the first live validation on the customer's machine.

## Before the session

- Confirm the exact Ophir device model and sensor head model.
- Confirm the vendor software/runtime version installed on the machine.
- Confirm the application is running as `x86`.
- Confirm the customer can open the device in the vendor tool first, if that tool is part of their normal setup.
- Confirm whether another process may already hold the device.

## Machine preparation

- Install the Ophir vendor automation package.
- Verify the COM ProgID `OphirLMMeasurement.CoLMMeasurement` is registered.
- Connect the device by USB and wait for Windows device initialization to finish.
- Close any application that may keep the device open, unless shared access is explicitly supported.

## In the app

- Select `Ophir SDK` as the second source for normal diagnostics.
- Run `Hardware Self-Test`.
- Run `Ophir Smoke-Test`.

## Expected outcomes

### Good outcome without device

- Runtime probe passes COM registration and COM activation.
- USB scan reports zero devices.
- Smoke-test says runtime is available but acquisition was skipped because no USB devices are visible.

### Good outcome with device

- USB scan reports at least one device.
- Device open passes.
- Sensor detection passes.
- Smoke-test reports that live samples were received.
- If capture is enabled, a CSV file is created in the configured capture directory.

## Artifacts to collect from the customer

- The full `Hardware Self-Test` report text.
- The full `Ophir Smoke-Test` report text.
- `application.log` from the app output directory.
- Any generated `ophir-smoke-test-*.txt` report.
- Any generated capture CSV from the `ophir-captures` directory.
- A screenshot of the source selection and diagnostics panel, if the text report alone is ambiguous.

## Questions to resolve during the live pass

- Does `ScanUSB` return the expected device count?
- Which channel has the active sensor head?
- Do timestamps from the SDK behave consistently enough to switch from host-arrival time to vendor time?
- Do status codes remain zero during normal acquisition?
- Are there sample gaps, duplicate samples, or delayed batches?

## If the smoke-test fails

- Re-run `Hardware Self-Test` first.
- Check whether the vendor tool can see the device.
- Check whether the bitness of the installed runtime matches the app.
- Check whether another process already opened the device.
- Save the reports and logs before changing multiple things at once.
