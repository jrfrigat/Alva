namespace Alva.Client;

/// <summary>Пункт навигации: адрес, подпись и иконка. Короткая подпись — для нижней навигации телефона,
/// где полное название не помещается; пустая означает «та же, что и полная».</summary>
public sealed record NavigationEntry(string Path, string Label, Flare.Icons.FlareIcon Icon, bool Primary, string? ShortLabel = null);

/// <summary>Раздел меню: заголовок и пункты под ним.</summary>
public sealed record NavigationGroup(string Title, NavigationEntry[] Entries);

/// <summary>Состав меню по макету Stitch (боковая панель p13/p14): пункт верхнего уровня,
/// разделы с заголовками и блок внизу списка. Адреса относительные: их резолвит базовый путь хоста,
/// поэтому демо в подпапке (/Alva/) открывает свои разделы, а не корень хостинга.</summary>
public static class NavigationItems
{
    public static readonly NavigationEntry Overview =
        new(AppRoutes.Home, Strings.T("Обзор"), Flare.Icons.MaterialDesign3Icons.Regular.Dashboard, true, Strings.T("Обзор"));

    public static readonly NavigationGroup[] Groups =
    [
        new(Strings.T("Планирование"),
        [
            new("sprints", Strings.T("Все спринты"), Flare.Icons.MaterialDesign3Icons.Regular.ViewTimeline, false),
            new("sprints/plan", Strings.T("Планирование спринта"), Flare.Icons.MaterialDesign3Icons.Regular.EventAvailable, false),
            new("projects", Strings.T("Проекты"), Flare.Icons.MaterialDesign3Icons.Regular.FolderSpecial, false)
        ]),
        new(Strings.T("Задачи"),
        [
            new("tasks", Strings.T("Дерево задач"), Flare.Icons.MaterialDesign3Icons.Regular.Layers, true, Strings.T("Задачи")),
            new("unassigned", Strings.T("Без исполнителя"), Flare.Icons.MaterialDesign3Icons.Regular.Inbox, false),
            new("my-work", Strings.T("Моя работа"), Flare.Icons.MaterialDesign3Icons.Regular.Checklist, false)
        ]),
        new(Strings.T("Диаграммы"),
        [
            new("gantt/tasks", Strings.T("Гантт по задачам"), Flare.Icons.MaterialDesign3Icons.Regular.WaterfallChart, false),
            new("gantt/people", Strings.T("Гантт по людям"), Flare.Icons.MaterialDesign3Icons.Regular.StackedBarChart, false)
        ]),
        new(Strings.T("Ёмкость"),
        [
            new("capacity", Strings.T("Обзор ёмкости"), Flare.Icons.MaterialDesign3Icons.Regular.Speed, true, Strings.T("Ёмкость")),
            new("capacity/people", Strings.T("Графики людей"), Flare.Icons.MaterialDesign3Icons.Regular.QueryStats, false)
        ]),
        new(Strings.T("Команда"),
        [
            new("team", Strings.T("Люди"), Flare.Icons.MaterialDesign3Icons.Regular.Badge, true, Strings.T("Люди")),
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
        new("data", Strings.T("Данные"), Flare.Icons.MaterialDesign3Icons.Regular.SwapVert, false),
        new("settings", Strings.T("Настройки"), Flare.Icons.MaterialDesign3Icons.Regular.Settings, false)
    ];

    /// <summary>Плоский список: пункт верхнего уровня, все разделы по порядку и хвост.</summary>
    public static readonly NavigationEntry[] All =
        [Overview, .. Groups.SelectMany(x => x.Entries), .. Tail];

    /// <summary>Разделы, попадающие в нижнюю навигацию PWA на mobile. Второй уровень раздела
    /// «Команда» сюда не входит: в нижней панели он не помещается.</summary>
    public static NavigationEntry[] Primary => All.Where(x => x.Primary).ToArray();
}
