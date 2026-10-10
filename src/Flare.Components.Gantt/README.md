# Flare.Components.Gantt

A universal timeline for Flare: Gantt charts, resource lanes, bookings, roadmaps — anything laid out over time in rows.

> Incubated in the Alva repository (Alva STORY-21) and to be moved into Flare (Flare EPIC-4). It references Flare
> only and knows nothing about the application that uses it. The Russian README (`README.ru.md`) is the primary one.

## Boundary
- **Draws and reports; does not compute the domain.** Capacity, overload, risk and people's schedules are the
  consumer's — it passes ready dates, values and statuses.
- **Never mutates the consumer's data.** Every user action arrives as a cancellable event carrying the changes;
  the consumer applies them.
- **Built-in calculations** are only generic graph geometry (`GanttGraph`): dependency cascade, critical path,
  cycle checks; plus summary roll-up (`GanttRollup`).

## Setup
```xml
<ProjectReference Include="..\Flare.Components.Gantt\Flare.Components.Gantt.csproj" />
```
```html
<link rel="stylesheet" href="_content/Flare.Components.Gantt/css/gantt.css" />
```

## Model
- `GanttSpan` — half-open [Start, End). Whole days use `Day` and `FromDays`; instants use `FromOffsets`.
- `GanttScale` — Hour, Day, Week, Month, Quarter or Year.
- Layers:
  - `GanttSegment`;
  - `GanttMarker`;
  - `GanttRegion`;
  - `GanttCell`;
  - `GanttLink` (FS/SS/FF/SF with lag);
  - `GanttChange`.
- Rows and items are **your own types** with selectors.
- A **Kind** is a string key, rendered as `flare-gantt--kind-{kind}` and `data-kind`. You decide how it looks.

## Components
- `FlareGantt` — the shared axis: range, scale, geometry, culture, non-working days, global markers and
  regions, selection, compact mode.
- `FlareGanttPanel<TRow, TItem>` — one projection: rows (list or tree), items, cells, regions, markers, links,
  critical path, suggestions, roll-up, and editing.

Put several panels in one `FlareGantt` to get a synchronised multi-projection view: they share one scale, one
scroll and one selection.

## Interaction
- **Pointer:** move and resize, with a snapped preview. Leaving the surface cancels the gesture.
- **Keyboard:**
  - Ctrl+←/→ moves;
  - Ctrl+Shift+←/→ resizes the end;
  - Ctrl+↑/↓ changes the row;
  - Enter/Space selects;
  - L starts a link, Enter or a click on the target creates it.
- **`OnChange`** carries the primary change plus the cascade.
- **`DropGroup`** turns every row × unit into a `FlareDropZone` for an outside `FlareDragContext`; read the
  target with `GanttDropTarget.TryParse`.

## Styling
Every look is a `--flare-gantt-*` variable falling back to shared Flare tokens. Set the variables on a kind's
class to style that kind.

## Deferred to the move into Flare
- `GanttTokens` in the theme.
- Rebasing the axis and lane engine on Flare's scheduler (Flare STORY-21).
- Row virtualisation.
- Baseline UI.
- Through a Flare JS port:
  - arrow-key focus;
  - scroll suppression;
  - pointer capture;
  - drag-to-link.
- Touch verification, Gallery pages and `docs/api`.
