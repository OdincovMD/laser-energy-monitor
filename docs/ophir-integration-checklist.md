# Ophir / StarLab integration checklist

## StarLab

- StarLab установлен и запускается на целевом ПК.
- Датчик Ophir автоматически подключается в StarLab.
- В StarLab включено логирование результатов.
- Файл `Data_log.txt` создается в ожидаемом каталоге.
- В файле есть заголовок таблицы и строки с измерениями.

## Laser Energy Monitor

- В блоке Ophir выбран тот же `Data_log.txt`, который обновляет StarLab.
- Во время сессии значение Ophir обновляется.
- Excel содержит разные значения Ophir в `RawData`.

## Если используется эмулятор

- Запущен `LaserEnergyMonitor.StarLabLogSimulator.exe`.
- Основное приложение подключено к файлу, который пишет эмулятор.
- Значения меняются в UI и экспортируются в Excel.
