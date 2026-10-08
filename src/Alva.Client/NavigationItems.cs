namespace Alva.Client;

public static class NavigationItems
{
    public static readonly (string Path, string Label, Flare.Icons.FlareIcon Icon)[] All =
    [
        ("/", "Обзор", Flare.Icons.MaterialDesign3Icons.Regular.Home),
        ("/sprints", "Спринты", Flare.Icons.MaterialDesign3Icons.Regular.Bolt),
        ("/tasks", "Задачи", Flare.Icons.MaterialDesign3Icons.Regular.Checklist),
        ("/diagrams", "Диаграммы", Flare.Icons.MaterialDesign3Icons.Regular.Timeline),
        ("/projects", "Проекты", Flare.Icons.MaterialDesign3Icons.Regular.Folder),
        ("/capacity", "Ёмкость", Flare.Icons.MaterialDesign3Icons.Regular.CalendarMonth),
        ("/team", "Команда", Flare.Icons.MaterialDesign3Icons.Regular.Group),
        ("/analytics", "Аналитика", Flare.Icons.MaterialDesign3Icons.Regular.BarChart),
        ("/my-work", "Моя работа", Flare.Icons.MaterialDesign3Icons.Regular.Person),
        ("/data", "Данные", Flare.Icons.MaterialDesign3Icons.Regular.DataObject),
        ("/settings", "Настройки", Flare.Icons.MaterialDesign3Icons.Regular.Settings)
    ];
}
