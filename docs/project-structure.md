# Структура solution и план реализации

## 1. Цель документа

Этот документ фиксирует практическую структуру проекта:

- как должна выглядеть `solution`;
- какие проекты в нее входят;
- какие классы нужны в первой версии;
- в каком порядке лучше реализовывать систему.

Документ нужен для того, чтобы перейти от ТЗ и архитектуры к реальной разработке без расплывчатых решений по ходу работы.

## 2. Рекомендуемая структура репозитория

```text
Автоматизация/
  docs/
    technical-specification.md
    architecture-and-stack.md
    project-structure.md
  src/
    LaserEnergyMonitor.Wpf/
    LaserEnergyMonitor.Application/
    LaserEnergyMonitor.Domain/
    LaserEnergyMonitor.Infrastructure/
    LaserEnergyMonitor.Infrastructure.BeamGage/
    LaserEnergyMonitor.Infrastructure.Ophir/
    LaserEnergyMonitor.Infrastructure.Excel/
    LaserEnergyMonitor.Infrastructure.Logging/
  tests/
    LaserEnergyMonitor.Tests/
  LaserEnergyMonitor.sln
```

## 3. Назначение проектов

### 3.1. `LaserEnergyMonitor.Wpf`

Это desktop-приложение с `WPF`.

Содержит:

- точку входа;
- главное окно;
- окна и диалоги;
- привязку UI к application services;
- отображение состояния системы;
- журнал событий для оператора.

Не должно содержать:

- прямую логику интеграции с оборудованием;
- логику стационарности;
- прямую запись в `XLSX`.

### 3.2. `LaserEnergyMonitor.Application`

Это orchestration-слой.

Содержит:

- сценарий запуска и остановки серии;
- управление жизненным циклом измерения;
- координацию между источниками данных, синхронизатором, детектором стационарности и экспортом;
- обработку ошибок на уровне приложения;
- DTO для UI.

Именно этот проект должен отвечать на вопрос "что делать дальше", а не "как работает конкретный прибор".

### 3.3. `LaserEnergyMonitor.Domain`

Это ядро бизнес-логики.

Содержит:

- доменные сущности;
- value objects;
- интерфейсы;
- enum и состояния;
- политику стационарности;
- модель событий серии измерений.

Этот проект не должен зависеть от UI, COM, Excel или конкретных библиотек.

### 3.4. `LaserEnergyMonitor.Infrastructure`

Общий инфраструктурный проект для:

- базовых утилит;
- общих адаптеров;
- системных абстракций;
- конфигурации;
- общих helper-компонентов.

Если окажется, что он не нужен как отдельный проект, его можно сократить, но на старте лучше оставить место для общей инфраструктуры.

### 3.5. `LaserEnergyMonitor.Infrastructure.BeamGage`

Содержит интеграцию с Beam Gage.

Содержит:

- adapter к Beam Gage automation API;
- инициализацию прибора;
- запуск и останов измерения;
- прием измерений;
- перевод данных в общий тип `MeasurementSample`.

### 3.6. `LaserEnergyMonitor.Infrastructure.Ophir`

Содержит интеграцию с `Ophir / Pulsar-4`.

Содержит:

- interop-обертку вокруг `OphirLMMeasurement` или другого доступного API;
- инициализацию устройства;
- управление потоком данных;
- прием энергии по trigger-based измерениям;
- перевод в общий тип `MeasurementSample`.

### 3.7. `LaserEnergyMonitor.Infrastructure.Excel`

Содержит экспорт в `XLSX`.

Содержит:

- writer для листа `RawData`;
- writer для листа `Events`;
- writer для листа `Summary`;
- компонент жизненного цикла файла.

### 3.8. `LaserEnergyMonitor.Infrastructure.Logging`

Содержит логирование.

Содержит:

- запись в файл;
- форматирование сообщений;
- адаптер для журналирования событий в UI;
- логирование аварий и технической диагностики.

### 3.9. `LaserEnergyMonitor.Tests`

Содержит автоматические тесты.

В первой версии здесь особенно важно тестировать:

- синхронизацию;
- буферы;
- стационарность;
- orchestration сценария;
- экспорт.

## 4. Зависимости между проектами

Рекомендуемая схема зависимостей:

```text
Wpf -> Application
Wpf -> Infrastructure.Logging

Application -> Domain
Application -> Infrastructure

Infrastructure.BeamGage -> Domain
Infrastructure.Ophir -> Domain
Infrastructure.Excel -> Domain
Infrastructure.Logging -> Domain

Tests -> Domain
Tests -> Application
Tests -> Infrastructure.Excel
```

Важно:

- `Domain` не зависит ни от кого;
- `Application` не должен зависеть от UI;
- интеграции с приборами не должны зависеть друг от друга;
- UI знает про `Application`, но не знает деталей vendor API.

## 5. Ключевые классы по проектам

## 5.1. `LaserEnergyMonitor.Domain`

### Сущности и модели

- `MeasurementSample`
- `SynchronizedMeasurementPair`
- `SessionEvent`
- `SessionSummary`
- `StationarityUpdate`
- `SessionMetadata`

### Enum и состояния

- `MeasurementSessionState`
- `SessionEventType`
- `DeviceHealthState`
- `SynchronizationState`
- `FaultSeverity`

### Интерфейсы

- `IMeasurementSource`
- `IMeasurementSynchronizer`
- `IStationarityDetector`
- `IStationarityPolicy`
- `IMeasurementExporter`
- `IApplicationLogger`
- `IOperatorNotifier`
- `IClock`

### Вспомогательные доменные компоненты

- `RollingWindow`
- `StationarityContext`
- `SynchronizationContext`

## 5.2. `LaserEnergyMonitor.Application`

### Основные сервисы

- `MeasurementSessionService`
- `SessionCoordinator`
- `SessionStateMachine`
- `MeasurementPipeline`
- `ErrorHandlingService`
- `SessionSettingsValidator`

### DTO и модели UI-уровня

- `SessionSettingsDto`
- `LiveMeasurementViewModel`
- `SessionStatusDto`
- `EventLogEntryDto`

### Use case / orchestration-команды

- `StartSessionCommand`
- `StopSessionCommand`
- `InitializeDevicesCommand`
- `ExportSessionCommand`

## 5.3. `LaserEnergyMonitor.Infrastructure.BeamGage`

### Основные классы

- `BeamGageMeasurementSource`
- `BeamGageClient`
- `BeamGageSampleMapper`
- `BeamGageConfiguration`
- `BeamGageExceptionTranslator`

Если у Beam Gage есть callback-модель:

- `BeamGageCallbackAdapter`

Если у Beam Gage только polling:

- `BeamGagePollingWorker`

## 5.4. `LaserEnergyMonitor.Infrastructure.Ophir`

### Основные классы

- `OphirMeasurementSource`
- `OphirLmMeasurementClient`
- `OphirSampleMapper`
- `OphirConfiguration`
- `OphirExceptionTranslator`

Если поток идет через `GetData` / `StartStream`:

- `OphirStreamWorker`

Если нужны COM event handlers:

- `OphirEventAdapter`

## 5.5. `LaserEnergyMonitor.Infrastructure.Excel`

### Основные классы

- `XlsxMeasurementExporter`
- `RawDataSheetWriter`
- `EventsSheetWriter`
- `SummarySheetWriter`
- `ExportFileNamingStrategy`
- `ExcelExportConfiguration`

## 5.6. `LaserEnergyMonitor.Infrastructure.Logging`

### Основные классы

- `FileApplicationLogger`
- `UiLogBuffer`
- `CompositeLogger`
- `LogEntryFormatter`

## 5.7. `LaserEnergyMonitor.Wpf`

### Основные окна

- `MainWindow`
- `ErrorDialog`
- `AboutDialog`

### Основные UI-компоненты

- `SessionSettingsControl`
- `LiveMeasurementsControl`
- `EventLogControl`
- `DeviceStatusControl`

### Служебные классы

- `MainWindowPresenter` или `MainWindowController`
- `UiThreadDispatcher`
- `ApplicationBootstrapper`

## 6. Минимальный MVP-состав

Для первой рабочей версии не нужно пытаться сделать все сразу.

Минимальный MVP:

- подключение к Beam Gage;
- подключение к Ophir / Pulsar-4;
- получение энергии с обоих трактов;
- синхронизация по времени;
- буфер последних `N`;
- расчет среднего;
- базовая детекция стационарности;
- журнал событий;
- экспорт в `XLSX`;
- простая форма `Start / Stop / Settings / Current Values`.

Во вторую очередь можно добавлять:

- расширенный экран диагностики;
- тонкую настройку политики стационарности;
- отдельный режим калибровки;
- более развитый summary-report;
- восстановление сессии после нештатного завершения.

## 7. Порядок реализации

Ниже рекомендованный порядок, который снижает риск.

## Этап 1. Проверка интеграций

Цель:

- убедиться, что оба прибора реально доступны из выбранного стека.

Что делаем:

- создаем технические spike-проекты;
- подключаем Beam Gage;
- подключаем Ophir / Pulsar-4;
- получаем по одному реальному измерению;
- фиксируем разрядность, способ инициализации и типы ошибок.

Результат этапа:

- подтвержденные интеграционные сценарии;
- подтвержденный target `x86`;
- понимание, callback там или polling.

## Этап 2. Каркас solution

Цель:

- развернуть правильную структуру проекта.

Что делаем:

- создаем solution;
- создаем проекты;
- настраиваем ссылки между проектами;
- добавляем базовые интерфейсы и модели;
- добавляем logger и конфигурацию.

Результат этапа:

- чистый skeleton приложения.

## Этап 3. Доменная модель и pipeline

Цель:

- реализовать ядро без привязки к UI.

Что делаем:

- `MeasurementSample`;
- `SynchronizedMeasurementPair`;
- `RollingWindow`;
- `MeasurementSynchronizer`;
- `StationarityDetector`;
- `SessionCoordinator`.

Результат этапа:

- рабочий pipeline на fake-данных.

## Этап 4. Тестовые fake-источники

Цель:

- научиться гонять сценарии без железа.

Что делаем:

- `FakeBeamGageSource`;
- `FakeOphirSource`;
- синхронный поток;
- рассинхрон;
- пропуск импульса;
- вход и выход из стационарности;
- fault-сценарии.

Результат этапа:

- повторяемые тесты бизнес-логики.

## Этап 5. Реальные драйверные адаптеры

Цель:

- подключить настоящее оборудование к готовому pipeline.

Что делаем:

- реализуем `BeamGageMeasurementSource`;
- реализуем `OphirMeasurementSource`;
- подключаем к `SessionCoordinator`;
- валидируем поток на реальных измерениях.

Результат этапа:

- первые реальные данные в общей модели.

## Этап 6. Экспорт

Цель:

- получать готовый `XLSX`.

Что делаем:

- `XlsxMeasurementExporter`;
- лист `RawData`;
- лист `Events`;
- лист `Summary`.

Результат этапа:

- проверяемый файл результата по серии.

## Этап 7. UI

Цель:

- дать оператору рабочий интерфейс.

Что делаем:

- `MainWindow`;
- старт и стоп;
- параметры `N`, порога и окна синхронизации;
- live values;
- state indicator;
- event log;
- уведомления об ошибках.

Результат этапа:

- операторское приложение, готовое к испытаниям.

## Этап 8. Полировка и эксплуатационные доработки

Цель:

- довести систему до удобного использования.

Что делаем:

- улучшение логов;
- поведение при ошибках;
- сохранение последних настроек;
- naming policy для файлов;
- инструкции оператору.

## 8. Приоритетные технические риски

До начала полноценной реализации нужно держать в фокусе следующие риски.

### 8.1. Риск совместимости библиотек

Есть риск, что:

- один SDK `x86`, другой `x64`;
- COM-компоненты требуют специфической регистрации;
- часть API доступна только при установленном vendor software.

Поэтому интеграционный spike обязателен.

### 8.2. Риск разной модели получения данных

В одном тракте может быть:

- callback;

а в другом:

- polling.

Это не проблема, но архитектура должна принимать оба варианта через единый `IMeasurementSource`.

### 8.3. Риск неочевидной синхронизации

Если приборы не дают единый `sequence id`, синхронизация будет идти через время, а значит:

- нужно аккуратно выбрать окно;
- возможны тонкие эффекты рассинхронизации;
- придется валидировать это на реальном эксперименте.

### 8.4. Риск объема `XLSX`

При длинной серии и высокой частоте запись должна быть потоковой, иначе можно получить:

- высокий расход памяти;
- задержки интерфейса;
- риск потери устойчивости.

## 9. Что я рекомендую фиксировать сразу

Перед началом кодинга лучше сразу договориться о следующих практических правилах:

- кодовая база создается сразу под `x86`;
- все vendor-зависимости изолируются в отдельных проектах;
- бизнес-логика не живет в `WPF`;
- алгоритм стационарности оформляется как заменяемая политика;
- запись в `XLSX` идет через отдельный exporter worker;
- UI получает только агрегированное безопасное состояние;
- любой device callback должен быстро отдавать данные в очередь и завершаться.

## 10. Ближайший практический шаг

После этого документа логично делать уже следующий артефакт:

- `solution bootstrap plan` или сразу scaffold solution.

Если идти в код, то следующая разумная задача:

1. создать `LaserEnergyMonitor.sln`;
2. поднять проекты из этой структуры;
3. завести базовые доменные модели и интерфейсы;
4. подготовить spike для подключения реальных SDK.
