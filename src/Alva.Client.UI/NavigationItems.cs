namespace Alva.Client;

/// <summary>Пункт навигации: адрес, подпись и иконка.</summary>
public sealed record NavigationEntry(string Path, string Label, Flare.Icons.FlareIcon Icon, bool Primary);

/// <summary>Раздел меню: заголовок и пункты под ним.</summary>
public sealed record NavigationGroup(string Title, NavigationEntry[] Entries);

/// <summary>Состав меню по макету Stitch (боковая панель p13/p14): пункт верхнего уровня,
/// разделы с заголовками и блок внизу списка.</summary>
public static class NavigationItems
{
    public static readonly NavigationEntry Overview =
        new("/", "Обзор (Дашборд)", Flare.Icons.MaterialDesign3Icons.Regular.Dashboard, true);

    public static readonly NavigationGroup[] Groups =
    [
        new("Спринты",
        [
            new("/sprints", "Список", Flare.Icons.MaterialDesign3Icons.Regular.ViewTimeline, false),
            new("/sprints/plan", "Планирование", Flare.Icons.MaterialDesign3Icons.Regular.EventAvailable, false)
        ]),
        new("Задачи",
        [
            new("/tasks", "«Матрёшка»", Flare.Icons.MaterialDesign3Icons.Regular.Layers, true),
            new("/unassigned", "Пул неназначенных", Flare.Icons.MaterialDesign3Icons.Regular.Inbox, false)
        ]),
        new("Диаграммы",
        [
            new("/diagrams", "Гантт по задачам", Flare.Icons.MaterialDesign3Icons.Regular.WaterfallChart, false),
            new("/projects", "Проекты", Flare.Icons.MaterialDesign3Icons.Regular.FolderSpecial, false)
        ]),
        new("Ёмкость",
        [
            new("/capacity", "Обзор ёмкости", Flare.Icons.MaterialDesign3Icons.Regular.Speed, true),
            new("/capacity/people", "Графики людей", Flare.Icons.MaterialDesign3Icons.Regular.QueryStats, false)
        ]),
        new("Команда",
        [
            new("/team", "Люди", Flare.Icons.MaterialDesign3Icons.Regular.Badge, true),
            new("/team/departments", "Подкоманды", Flare.Icons.MaterialDesign3Icons.Regular.Hub, false),
            new("/team/roles", "Роли", Flare.Icons.MaterialDesign3Icons.Regular.ManageAccounts, false)
        ]),
        new("Аналитика",
        [
            new("/analytics", "Риски и причины", Flare.Icons.MaterialDesign3Icons.Regular.Warning, false)
        ])
    ];

    /// <summary>Блок под разделами, как в макете: после групп, отделён линией.</summary>
    public static readonly NavigationEntry[] Tail =
    [
        new("/my-work", "Моя работа", Flare.Icons.MaterialDesign3Icons.Regular.Checklist, false),
        new("/data", "Данные (Импорт/Экспорт)", Flare.Icons.MaterialDesign3Icons.Regular.SwapVert, false),
        new("/settings", "Настройки", Flare.Icons.MaterialDesign3Icons.Regular.Settings, false)
    ];

    /// <summary>Плоский список: пункт верхнего уровня, все разделы по порядку и хвост.</summary>
    public static readonly NavigationEntry[] All =
        [Overview, .. Groups.SelectMany(x => x.Entries), .. Tail];

    /// <summary>Разделы, попадающие в нижнюю навигацию PWA на mobile. Второй уровень раздела
    /// «Команда» сюда не входит: в нижней панели он не помещается.</summary>
    public static NavigationEntry[] Primary => All.Where(x => x.Primary).ToArray();
}

