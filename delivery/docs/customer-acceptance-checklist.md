# Laser Energy Monitor: приемочная проверка

## Цель

Подтвердить запуск приложения, подключение целевых источников и запись результата в Excel.

## Порядок проверки

1. Распаковать поставку в локальную папку с правами на запись.
2. Запустить `app\LaserEnergyMonitor.Wpf.exe`.
3. Нажать `Scan`, выбрать физический BeamGage source и нажать `Connect`.
4. В StarLab подключить Ophir и включить логирование результатов.
5. Указать файл `Data_log.txt` в блоке Ophir.
6. Проверить параметры `Window`, `Enter %`, `Exit %` и `Output Path`.
7. Нажать `Start` и провести короткую сессию на 30-60 секунд.
8. Нажать `Stop`.
9. Проверить итоговый `measurement-session.xlsx` и `measurement-session.RawData.csv`.

## Критерии успеха

- Приложение запускается без ошибки.
- BeamGage source подключается через `Connect`.
- StarLab пишет новые строки в `Data_log.txt`.
- В UI меняются значения Beam и Ophir.
- В Excel записаны строки с разными значениями Ophir.
- В `RawData.csv` есть колонки `FirstStabilityMetricPercent`, `SecondStabilityMetricPercent`, `FirstIsStationary`, `SecondIsStationary`, `IsStationary`.

## Что вернуть разработчику

- `application.log`
- `measurement-session.xlsx`
- `measurement-session.*.csv`
- фрагмент `Data_log.txt`, если проблема связана с Ophir
- модели подключенных датчиков и выбранный BeamGage source
