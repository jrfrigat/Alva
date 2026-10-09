namespace Alva.Client;

public sealed record NavigationEntry(string Path, string Label, Flare.Icons.FlareIcon Icon, bool Primary);

public static class NavigationItems
{
    public static readonly NavigationEntry[] All =
    [
        new("/", "Обзор", Flare.Icons.MaterialDesign3Icons.Regular.Home, true),
        new("/sprints", "Спринты", Flare.Icons.MaterialDesign3Icons.Regular.Bolt, false),
        new("/tasks", "Задачи", Flare.Icons.MaterialDesign3Icons.Regular.Checklist, true),
        new("/diagrams", "Диаграммы", Flare.Icons.MaterialDesign3Icons.Regular.Timeline, false),
        new("/projects", "Проекты", Flare.Icons.MaterialDesign3Icons.Regular.Folder, false),
        new("/capacity", "Ёмкость", Flare.Icons.MaterialDesign3Icons.Regular.CalendarMonth, true),
        new("/team", "Команда", Flare.Icons.MaterialDesign3Icons.Regular.Group, true),
        new("/analytics", "Аналитика", Flare.Icons.MaterialDesign3Icons.Regular.BarChart, false),
        new("/my-work", "Моя работа", Flare.Icons.MaterialDesign3Icons.Regular.Person, false),
        new("/data", "Данные", Flare.Icons.MaterialDesign3Icons.Regular.DataObject, false),
        new("/settings", "Настройки", Flare.Icons.MaterialDesign3Icons.Regular.Settings, false)
    ];

    /// <summary>Разделы, попадающие в нижнюю навигацию PWA на mobile.</summary>
    public static NavigationEntry[] Primary => All.Where(x => x.Primary).ToArray();
}

