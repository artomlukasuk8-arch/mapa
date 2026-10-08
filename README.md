# MapSuite

Це стартовий каркас для двох застосунків:

1. `MapBuilder` — десктопний C#-редактор для завантаження `.conf`, формування карти, додавання маркерів, обробки 22 фото та експорту в GitHub-совместимий пакет.
2. `MapViewer` — Android MAUI-додаток, який у майбутньому буде завантажувати `manifest.json` з GitHub і відображати карту, маркери та 360-панорами.

## Структура

- `MapCore` — спільна логіка і типи даних.
- `MapBuilder` — Windows WPF-додаток.
- `MapViewer` — Android MAUI scaffold.

## Запуск desktop-версії

```bash
dotnet run --project MapBuilder/MapBuilder.csproj
```

## Запуск Android-проекту

Після встановлення MAUI workload:

```bash
dotnet workload install maui
dotnet build MapViewer/MapViewer.csproj -f net8.0-android
```

## Далі

- розширити парсер `.conf` і виділити базові параметри карти;
- додати збереження міток і стилів шарів;
- згенерувати GitHub-публікацію через PAT;
- додати Android-логіку завантаження `manifest.json` і панорам;
- реалізувати 360-обертання виду у MAUI.
