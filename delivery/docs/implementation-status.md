# Текущий статус реализации

## Актуально реализовано

- WPF-приложение `LaserEnergyMonitor.Wpf` под `.NET Framework 4.8`.
- Целевая платформа `x86`.
- BeamGage путь: `BeamGage SDK` -> `Scan` -> выбор source -> `Connect`.
- Ophir путь: чтение `Data_log.txt`, который пишет StarLab.
- Экспорт результатов в `.xlsx` и диагностические CSV.
- Файловое логирование и отчеты диагностики.
- `StarLabLogSimulator` для локальной проверки без реального Ophir.

## Удалено как неактуальное

- Прямое подключение Ophir через vendor runtime.
- Legacy ActiveX путь.
- Ophir replay/capture режим.
- Встроенные source-симуляции в основном приложении.
- Старые тесты Ophir FastX.
- Старый отдельный probe для прямого Ophir runtime.

## Локальная проверка

- `dotnet build LaserEnergyMonitor.sln -p:Platform=x86 --no-restore` - успешно, 0 warnings, 0 errors.
- `dotnet test LaserEnergyMonitor.sln -p:Platform=x86 --no-restore` - успешно, 23 passed.

## Риски до проверки на стенде

- BeamGage должен увидеть физические источники на целевом ПК.
- StarLab должен стабильно писать `Data_log.txt`.
- Нужно подтвердить реальный формат колонок StarLab у заказчика, особенно колонку энергии `Math M`.
- При flush раз в несколько секунд значения будут приходить пачками, это ожидаемое поведение.
