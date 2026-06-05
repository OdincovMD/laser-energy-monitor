# Laser Energy Monitor

`Laser Energy Monitor` — настольное приложение для сбора, синхронизации и экспорта результатов измерений с поддержкой симуляции и интеграций с оборудованием `BeamGage` и `Ophir`.

Проект распространяется по лицензии [MIT](LICENSE).

## Что это

- основная точка входа приложения находится в `src/LaserEnergyMonitor.Wpf`;
- бизнес-логика и оркестрация сценариев вынесены в `src/LaserEnergyMonitor.Application`;
- доменная модель и алгоритмы находятся в `src/LaserEnergyMonitor.Domain`;
- интеграции, экспорт и логирование собраны в `src/LaserEnergyMonitor.Infrastructure*`;
- unit-тесты лежат в `tests/LaserEnergyMonitor.Tests`.

## Текущий стек

- платформа: `.NET Framework 4.8`;
- целевая разрядность: `x86`;
- UI: `WPF`;
- режимы работы: симуляция, `BeamGage`, `Ophir`.

Подробнее о причинах такого выбора: [Архитектура и стек](docs/architecture-and-stack.md).

## Быстрый старт

1. Откройте `LaserEnergyMonitor.sln` в Visual Studio 2022 или соберите решение через MSBuild.
2. Выберите конфигурацию `Release|x86`.
3. Проверьте зависимости для нужного режима работы:
   - для симуляции дополнительные runtime от вендора не нужны;
   - для реального `BeamGage` и `Ophir` нужны соответствующие компоненты вендора.
4. Запустите проект `LaserEnergyMonitor.Wpf`.

## Сборка

Типичный сценарий сборки:

```powershell
msbuild LaserEnergyMonitor.sln /p:Configuration=Release /p:Platform=x86
```

## Документация

- [Структура проекта](docs/project-structure.md)
- [Архитектура и стек](docs/architecture-and-stack.md)
- [Статус реализации](docs/implementation-status.md)
- [Комплект поставки](docs/release-bundle-notes.md)
- [Первичная проверка BeamGage](docs/beamgage-first-run-validation.md)
- [Первичная проверка Ophir](docs/ophir-first-run-validation.md)
- [Ophir Pulsar ActiveX validation](docs/ophir-pulsar-activex-validation.md)
- [Как участвовать](CONTRIBUTING.md)
- [Политика безопасности](SECURITY.md)
- [Кодекс поведения](CODE_OF_CONDUCT.md)
- [Чеклист релиза](RELEASE_CHECKLIST.md)
- [Список изменений](RELEASE_NOTES.md)

## Полезно перед проверкой стенда

- сначала сверяйте [Статус реализации](docs/implementation-status.md);
- если готовите выкладку, смотрите [Комплект поставки](docs/release-bundle-notes.md);
- если меняете состав папок или проектов, обновляйте [Структуру проекта](docs/project-structure.md).
