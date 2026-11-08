namespace Alva.Client;

/// <summary>A navigation entry: address, label and icon. The short label is for the mobile bottom navigation,
/// where the full name does not fit; empty means "the same as the full one".</summary>
public sealed record NavigationEntry(string Path, string Label, Flare.Icons.FlareIcon Icon, bool Primary, string? ShortLabel = null);

/// <summary>A menu section: a heading and the entries under it.</summary>
public sealed record NavigationGroup(string Title, NavigationEntry[] Entries);

/// <summary>The menu layout from the Stitch design (side panel p13/p14): a top-level entry,
/// sections with headings and a block at the bottom of the list. Addresses are relative: the host base path
/// resolves them, so the demo in a subfolder (/Alva/) opens its own sections rather than the hosting root.</summary>
public static class NavigationItems
{
    public static readonly NavigationEntry Overview =
        new(AppRoutes.Home, AlvaStrings.Nav_Overview, Flare.Icons.MaterialDesign3Icons.Regular.Dashboard, true, AlvaStrings.Nav_Overview);

    public static readonly NavigationGroup[] Groups =
    [
        new(AlvaStrings.Common_Planning,
        [
            new("sprints", AlvaStrings.Common_AllSprints, Flare.Icons.MaterialDesign3Icons.Regular.ViewTimeline, false),
            new("sprints/plan", AlvaStrings.Sprint_PlanningASprint, Flare.Icons.MaterialDesign3Icons.Regular.EventAvailable, false),
            new("projects", AlvaStrings.Common_Projects, Flare.Icons.MaterialDesign3Icons.Regular.FolderSpecial, false)
        ]),
        new(AlvaStrings.Common_Tasks,
        [
            new("tasks", AlvaStrings.Nav_TaskTree, Flare.Icons.MaterialDesign3Icons.Regular.Layers, true, AlvaStrings.Common_Tasks),
            new("unassigned", AlvaStrings.Common_Unassigned, Flare.Icons.MaterialDesign3Icons.Regular.Inbox, false),
            new("my-work", AlvaStrings.Tasks_MyWork, Flare.Icons.MaterialDesign3Icons.Regular.Checklist, false)
        ]),
        new(AlvaStrings.Nav_Charts,
        [
            new("gantt/tasks", AlvaStrings.Gantt_TaskGantt, Flare.Icons.MaterialDesign3Icons.Regular.AlignHorizontalLeft, false),
            new("gantt/people", AlvaStrings.Gantt_PeopleGantt, Flare.Icons.MaterialDesign3Icons.Regular.StackedBarChart, false)
        ]),
        new(AlvaStrings.Common_Capacity,
        [
            new("capacity", AlvaStrings.Nav_CapacityOverview, Flare.Icons.MaterialDesign3Icons.Regular.Speed, true, AlvaStrings.Common_Capacity),
            new("capacity/people", AlvaStrings.Common_PeopleSchedules, Flare.Icons.MaterialDesign3Icons.Regular.QueryStats, false)
        ]),
        new(AlvaStrings.Common_Team,
        [
            new("team", AlvaStrings.Nav_People, Flare.Icons.MaterialDesign3Icons.Regular.Badge, true, AlvaStrings.Nav_People),
            new("team/departments", AlvaStrings.Common_SubTeams, Flare.Icons.MaterialDesign3Icons.Regular.Hub, false),
            new("team/skills", AlvaStrings.Common_Skills, Flare.Icons.MaterialDesign3Icons.Regular.ManageAccounts, false)
        ]),
        new(AlvaStrings.Nav_Analytics,
        [
            new("analytics", AlvaStrings.Nav_RisksAndReasons, Flare.Icons.MaterialDesign3Icons.Regular.Warning, false)
        ])
    ];

    /// <summary>The block under the sections, as in the design: after the groups, separated by a line.</summary>
    public static readonly NavigationEntry[] Tail =
    [
        new("data", AlvaStrings.Nav_Data, Flare.Icons.MaterialDesign3Icons.Regular.SwapVert, false),
        new("integrations", AlvaStrings.Common_Integrations, Flare.Icons.MaterialDesign3Icons.Regular.Sync, false),
        new("settings", AlvaStrings.Common_Settings, Flare.Icons.MaterialDesign3Icons.Regular.Settings, false)
    ];

    /// <summary>A flat list: the top-level entry, all sections in order and the tail.</summary>
    public static readonly NavigationEntry[] All =
        [Overview, .. Groups.SelectMany(x => x.Entries), .. Tail];

    /// <summary>The sections included in the mobile PWA bottom navigation. The second level of the
    /// The Team section is not included: it does not fit in the bottom bar.</summary>
    public static NavigationEntry[] Primary => All.Where(x => x.Primary).ToArray();
}
