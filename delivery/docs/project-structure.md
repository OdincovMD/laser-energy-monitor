# Структура проекта

```text
laser-energy-monitor/
  docs/
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
  tools/
    StarLabLogSimulator/
  LaserEnergyMonitor.sln
```

## Основные проекты

### `LaserEnergyMonitor.Wpf`

WPF-приложение оператора. Содержит экран подключения, выбор BeamGage source, выбор StarLab log file, запуск сессии и отображение текущих значений.

### `LaserEnergyMonitor.Application`

Оркестрация измерительной сессии: жизненный цикл источников, обработка samples, события, экспорт и уведомления.

### `LaserEnergyMonitor.Domain`

Доменная модель измерений, события сессии, стационарность, rolling window и контракты источников.

### `LaserEnergyMonitor.Infrastructure`

Общая инфраструктура. Сейчас содержит системные сервисы вроде `SystemClock`.

### `LaserEnergyMonitor.Infrastructure.BeamGage`

Интеграция с BeamGage SDK: runtime probe, сканирование источников, подключение выбранного source, чтение энергии.

### `LaserEnergyMonitor.Infrastructure.Ophir`

Интеграция Ophir через StarLab log: `StarLabLogMeasurementSource`, `StarLabLogMeasurementOptions`, парсер `Data_log.txt`.

### `LaserEnergyMonitor.Infrastructure.Excel`

Экспорт `.xlsx` и диагностических CSV.

### `LaserEnergyMonitor.Infrastructure.Logging`

Файловое логирование приложения.

### `LaserEnergyMonitor.Tests`

Unit-тесты домена, application-слоя, логирования, BeamGage helpers и StarLab log reader.

## Инструменты

- `StarLabLogSimulator` - генератор `Data_log.txt` для проверки Ophir log-file режима без прибора.
