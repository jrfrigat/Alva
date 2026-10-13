namespace Alva.Client;

/// <summary>Пункт навигации: адрес, подпись и иконка.</summary>
public sealed record NavigationEntry(string Path, string Label, Flare.Icons.FlareIcon Icon, bool Primary);

/// <summary>Раздел меню: заголовок и пункты под ним.</summary>
public sealed record NavigationGroup(string Title, NavigationEntry[] Entries);

/// <summary>Состав меню по макету Stitch (боковая панель p13/p14): пункт верхнего уровня,
/// разделы с заголовками и блок внизу списка. Адреса относительные: их резолвит базовый путь хоста,
/// поэтому демо в подпапке (/Alva/) открывает свои разделы, а не корень хостинга.</summary>
public static class NavigationItems
{
    public static readonly NavigationEntry Overview =
        new(AppRoutes.Home, Strings.T("Обзор (Дашборд)"), Flare.Icons.MaterialDesign3Icons.Regular.Dashboard, true);

    public static readonly NavigationGroup[] Groups =
    [
        new(Strings.T("Спринты"),
        [
            new("sprints", Strings.T("Список"), Flare.Icons.MaterialDesign3Icons.Regular.ViewTimeline, false),
            new("sprints/plan", Strings.T("Планирование"), Flare.Icons.MaterialDesign3Icons.Regular.EventAvailable, false)
        ]),
        new("Задачи",
        [
            new("tasks", Strings.T("Дерево задач"), Flare.Icons.MaterialDesign3Icons.Regular.Layers, true),
            new("unassigned", Strings.T("Пул неназначенных"), Flare.Icons.MaterialDesign3Icons.Regular.Inbox, false)
        ]),
        new(Strings.T("Диаграммы"),
        [
            new("gantt/tasks", Strings.T("Гантт по задачам"), Flare.Icons.MaterialDesign3Icons.Regular.WaterfallChart, false),
            new("gantt/people", Strings.T("Гантт по людям"), Flare.Icons.MaterialDesign3Icons.Regular.StackedBarChart, false),
            new("projects", Strings.T("Проекты"), Flare.Icons.MaterialDesign3Icons.Regular.FolderSpecial, false)
        ]),
        new(Strings.T("Ёмкость"),
        [
            new("capacity", Strings.T("Обзор ёмкости"), Flare.Icons.MaterialDesign3Icons.Regular.Speed, true),
            new("capacity/people", Strings.T("Графики людей"), Flare.Icons.MaterialDesign3Icons.Regular.QueryStats, false)
        ]),
        new("Команда",
        [
            new("team", Strings.T("Люди"), Flare.Icons.MaterialDesign3Icons.Regular.Badge, true),
            new("team/departments", Strings.T("Подкоманды"), Flare.Icons.MaterialDesign3Icons.Regular.Hub, false),
            new("team/roles", Strings.T("Роли"), Flare.Icons.MaterialDesign3Icons.Regular.ManageAccounts, false)
        ]),
        new(Strings.T("Аналитика"),
        [
            new("analytics", Strings.T("Риски и причины"), Flare.Icons.MaterialDesign3Icons.Regular.Warning, false)
        ])
    ];

    /// <summary>Блок под разделами, как в макете: после групп, отделён линией.</summary>
    public static readonly NavigationEntry[] Tail =
    [
        new("my-work", Strings.T("Моя работа"), Flare.Icons.MaterialDesign3Icons.Regular.Checklist, false),
        new("data", Strings.T("Данные (Импорт/Экспорт)"), Flare.Icons.MaterialDesign3Icons.Regular.SwapVert, false),
        new("settings", Strings.T("Настройки"), Flare.Icons.MaterialDesign3Icons.Regular.Settings, false)
    ];

    /// <summary>Плоский список: пункт верхнего уровня, все разделы по порядку и хвост.</summary>
    public static readonly NavigationEntry[] All =
        [Overview, .. Groups.SelectMany(x => x.Entries), .. Tail];

    /// <summary>Разделы, попадающие в нижнюю навигацию PWA на mobile. Второй уровень раздела
    /// «Команда» сюда не входит: в нижней панели он не помещается.</summary>
    public static NavigationEntry[] Primary => All.Where(x => x.Primary).ToArray();
}

