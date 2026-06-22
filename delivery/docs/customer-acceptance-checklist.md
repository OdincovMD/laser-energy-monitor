# Laser Energy Monitor: приемочная проверка

## Цель

Подтвердить запуск приложения, подключение целевых источников и запись результата в Excel.

## Порядок проверки

1. Распаковать поставку в локальную папку с правами на запись.
2. Запустить `app\LaserEnergyMonitor.Wpf.exe`.
3. Для Beam выбрать `BeamGage SDK`.
4. Нажать `Scan`, выбрать физический источник и нажать `Connect`.
5. В StarLab подключить Ophir и включить логирование результатов.
6. Для Ophir выбрать `StarLab Log File`.
7. Указать файл `Data_log.txt`.
8. Нажать `Self-Test`.
9. Запустить короткую сессию на 30-60 секунд.
10. Проверить итоговый `measurement-session.xlsx` и `measurement-session.RawData.csv`.

## Критерии успеха

- Приложение запускается без ошибки.
- BeamGage source подключается через `Connect`.
- StarLab пишет новые строки в `Data_log.txt`.
- `Self-Test` видит StarLab log file и колонку энергии.
- В UI меняются значения Beam и Ophir.
- В Excel записаны строки с разными значениями Ophir.
- В `RawData.csv` есть колонки `FirstStabilityMetricPercent`, `SecondStabilityMetricPercent`, `FirstIsStationary`, `SecondIsStationary`, `IsStationary`.

## Что вернуть разработчику

- `application.log`
- `hardware-self-test-*.txt`
- `usb-inventory-*.txt`, если запускался `USB Devices`
- `beamgage-smoke-test-*.txt`, если запускался `BeamGage Test`
- `measurement-session.xlsx`
- `measurement-session.*.csv`
- фрагмент `Data_log.txt`, если проблема связана с Ophir
- модели подключенных датчиков и выбранные источники в UI
