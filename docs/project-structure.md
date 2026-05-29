# Структура проекта

## Актуальная структура репозитория

```text
laser-energy-monitor/
  delivery/
    README.md
  docs/
    architecture-and-stack.md
    customer-delivery-readme.md
    customer-ophir-first-run.md
    implementation-status.md
    ophir-integration-checklist.md
    ophir-setup.md
    project-structure.md
    technical-specification.md
  src/
    LaserEnergyMonitor.Application/
    LaserEnergyMonitor.Domain/
    LaserEnergyMonitor.Infrastructure/
    LaserEnergyMonitor.Infrastructure.BeamGage/
    LaserEnergyMonitor.Infrastructure.Excel/
    LaserEnergyMonitor.Infrastructure.Logging/
    LaserEnergyMonitor.Infrastructure.Ophir/
    LaserEnergyMonitor.Wpf/
  tests/
    LaserEnergyMonitor.Tests/
  Directory.Build.props
  LaserEnergyMonitor.sln
  NuGet.Config
  ui-tests.ps1
```

## Проекты в solution

### `LaserEnergyMonitor.Wpf`

Основное desktop-приложение.

Содержит:

- `App.xaml`
- `MainWindow.xaml`
- `Runtime/MeasurementSessionRuntimeFactory.cs`
- wiring UI с application/domain/infrastructure слоями

### `LaserEnergyMonitor.Application`

Application-слой orchestration.

Содержит:

- `MeasurementSessionService`
- `SessionSettingsValidator`

### `LaserEnergyMonitor.Domain`

Доменная модель измерений.

Содержит:

- интерфейсы источников, экспортера и уведомлений
- модели samples, session events, summaries
- `RollingWindow`
- `RollingStationarityDetector`
- `TimeWindowMeasurementSynchronizer`

### `LaserEnergyMonitor.Infrastructure`

Общие инфраструктурные реализации.

Содержит:

- `SystemClock`
- simulated sources и measurement profiles

### `LaserEnergyMonitor.Infrastructure.BeamGage`

Интеграция с BeamGage.

Содержит:

- runtime probe
- session wrapper
- measurement source
- timestamp/options/prerequisite classes

### `LaserEnergyMonitor.Infrastructure.Ophir`

Интеграция с `Ophir / Pulsar-4`.

Содержит:

- `OphirRuntimeProbe`
- `OphirRuntimeSession`
- `OphirMeasurementSource`
- `OphirReplayMeasurementSource`
- `OphirCaptureWriter`
- `StaWorker`

### `LaserEnergyMonitor.Infrastructure.Excel`

Экспорт результатов.

Содержит:

- `PrototypeExcelExporter`

Важно:

- имя класса историческое;
- на практике он уже создает итоговый `.xlsx` через `Open XML`;
- при этом рядом остаются shadow CSV-файлы для диагностики.

### `LaserEnergyMonitor.Infrastructure.Logging`

Файловое логирование.

Содержит:

- `FileApplicationLogger`

### `LaserEnergyMonitor.Tests`

Unit-тесты на доменную и application-логику.

Содержит:

- `DomainBehaviorTests`
- `MeasurementSessionServiceTests`
- `TestDoubles`

## Вне solution, но важно

- `docs/` содержит пользовательскую и техническую документацию;
- `delivery/README.md` описывает структуру итоговой клиентской поставки;
- `ui-tests.ps1` хранит локальный сценарий UI-проверок.

## Что не должно считаться частью исходников

В репозитории не должны храниться:

- `bin/`
- `obj/`
- `output/`
- `ui-test-artifacts/`
- локальные клиентские выкладки в `delivery/LaserEnergyMonitor-*`
- следы старого проекта `LaserEnergyMonitor.App`, если в каталоге остались только артефакты сборки

Эти каталоги считаются локальными временными файлами и могут безопасно удаляться перед передачей репозитория или повторной сборкой.
