using Flare.Abstractions.Tokens;

namespace Alva.Theme.Alva;

/// <summary>
/// Палитра Alva — «Alva Sapphire». Светлая схема перенесена из дизайн-источника Stitch
/// (<c>.aiko/stitch_alva_resource_planner_pwa/alva_strategic_resource_platform/DESIGN.md</c>) значение
/// в значение.
/// </summary>
/// <remarks>
/// Три группы ролей дизайн-файл не называет, и они собраны из его собственных значений:
/// <list type="bullet">
/// <item><description><b>Success / Warning / Info</b> — из семантической палитры дизайна: зелёный
/// «Balanced Pill» (<c>#10B981</c> / контейнер <c>#D1FAE5</c> / текст <c>#065F46</c>), янтарный
/// «Warning» (<c>#F59E0B</c> / <c>#FEF3C7</c> / <c>#D97706</c>), информационный — вторичная
/// семья (<c>#3755C3</c> / <c>#DCE1FF</c> / <c>#0039B5</c>).</description></item>
/// <item><description><b>Тёмная схема</b> — дизайн-источник описывает только светлую. Тёмная собрана
/// из его же ролей (<c>inverse-surface #213145</c>, <c>inverse-on-surface #EAF1FF</c>,
/// <c>inverse-primary #B7C4FF</c>) и нейтральной лестницы в том же синем тоне. Это
/// интерпретация, а не значение из файла дизайна: при появлении тёмного макета её нужно сверить
/// с ним, а не считать истиной.</description></item>
/// <item><description><b>Fixed-лестницы</b> — одинаковы в обеих схемах: контент, окрашенный
/// fixed-ролью, не должен менять цвет между режимами (это проверяет <c>ThemeSpecs</c>), поэтому
/// берутся светлые значения дизайна в обоих режимах.</description></item>
/// </list>
/// </remarks>
public static class AlvaPalettes
{
    /// <summary>Стабильный id палитры — им тема называет палитру по умолчанию.</summary>
    public const string SapphireId = "alva-sapphire";

    /// <summary>Источник палитры для переключателей.</summary>
    public const string SourceName = "Alva";

    private static readonly ColorScheme Light = new()
    {
        // Акцент: сапфировый индиго, вторичный — синий, третичный — фиолетовый.
        Primary = "#0037B0",
        OnPrimary = "#FFFFFF",
        PrimaryContainer = "#1D4ED8",
        OnPrimaryContainer = "#CAD3FF",
        Secondary = "#3755C3",
        OnSecondary = "#FFFFFF",
        SecondaryContainer = "#708CFD",
        OnSecondaryContainer = "#00217A",
        Tertiary = "#5311BC",
        OnTertiary = "#FFFFFF",
        TertiaryContainer = "#6B38D4",
        OnTertiaryContainer = "#DDCDFF",

        // Семантика.
        Error = "#BA1A1A",
        OnError = "#FFFFFF",
        ErrorContainer = "#FFDAD6",
        OnErrorContainer = "#93000A",
        Success = "#10B981",
        OnSuccess = "#FFFFFF",
        SuccessContainer = "#D1FAE5",
        OnSuccessContainer = "#065F46",
        Warning = "#F59E0B",
        OnWarning = "#FFFFFF",
        WarningContainer = "#FEF3C7",
        OnWarningContainer = "#D97706",
        Info = "#3755C3",
        OnInfo = "#FFFFFF",
        InfoContainer = "#DCE1FF",
        OnInfoContainer = "#0039B5",

        // Плоскости: лестница дизайна от белого до самого плотного синего.
        Surface = "#F8F9FF",
        OnSurface = "#0B1C30",
        SurfaceVariant = "#D3E4FE",
        OnSurfaceVariant = "#434655",
        OnSurfaceVariant2 = "#747686",
        SurfaceContainerLowest = "#FFFFFF",
        SurfaceContainerLow = "#EFF4FF",
        SurfaceContainer = "#E5EEFF",
        SurfaceContainerHigh = "#DCE9FF",
        SurfaceContainerHighest = "#D3E4FE",
        Background = "#F8F9FF",
        OnBackground = "#0B1C30",
        SurfaceBright = "#F8F9FF",
        SurfaceDim = "#CBD8F5",

        // Границы и инверсия.
        Outline = "#747686",
        OutlineVariant = "#C4C5D7",
        InverseSurface = "#213145",
        InverseOnSurface = "#EAF1FF",
        InversePrimary = "#B7C4FF",
        Scrim = "#000000",
        Shadow = "#000000",
        // Дизайн тени не рисует, а где рисует — тонирует их тем же слейтом, что и текст.
        ShadowUmbra = "rgba(15, 23, 42, 0.08)",
        ShadowPenumbra = "rgba(15, 23, 42, 0.04)",

        // Fixed-лестницы: значения дизайна, одинаковые в обеих схемах.
        PrimaryFixed = "#DCE1FF",
        PrimaryFixedDim = "#B7C4FF",
        OnPrimaryFixed = "#001551",
        OnPrimaryFixedVariant = "#0039B5",
        SecondaryFixed = "#DDE1FF",
        SecondaryFixedDim = "#B8C4FF",
        OnSecondaryFixed = "#001453",
        OnSecondaryFixedVariant = "#173BAB",
        TertiaryFixed = "#E9DDFF",
        TertiaryFixedDim = "#D0BCFF",
        OnTertiaryFixed = "#23005C",
        OnTertiaryFixedVariant = "#5516BE",
    };

    /// <summary>
    /// Тёмная схема: дизайн-источник её не описывает, значения собраны из его же inverse- и
    /// fixed-ролей и нейтральной лестницы в том же синем тоне (см. примечание к классу).
    /// </summary>
    private static readonly ColorScheme Dark = new()
    {
        Primary = "#B7C4FF",
        OnPrimary = "#001551",
        PrimaryContainer = "#1D4ED8",
        OnPrimaryContainer = "#CAD3FF",
        Secondary = "#B8C4FF",
        OnSecondary = "#001453",
        SecondaryContainer = "#3755C3",
        OnSecondaryContainer = "#DDE1FF",
        Tertiary = "#D0BCFF",
        OnTertiary = "#23005C",
        TertiaryContainer = "#6B38D4",
        OnTertiaryContainer = "#E9DDFF",

        Error = "#FFB4AB",
        OnError = "#690005",
        ErrorContainer = "#93000A",
        OnErrorContainer = "#FFDAD6",
        Success = "#6EE7B7",
        OnSuccess = "#00382E",
        SuccessContainer = "#065F46",
        OnSuccessContainer = "#D1FAE5",
        Warning = "#FCD34D",
        OnWarning = "#4E2513",
        WarningContainer = "#D97706",
        OnWarningContainer = "#FEF3C7",
        Info = "#B7C4FF",
        OnInfo = "#001551",
        InfoContainer = "#2151DA",
        OnInfoContainer = "#DCE1FF",

        Surface = "#0B1C30",
        OnSurface = "#EAF1FF",
        SurfaceVariant = "#213145",
        OnSurfaceVariant = "#C4C5D7",
        OnSurfaceVariant2 = "#8B93A5",
        SurfaceContainerLowest = "#06121F",
        SurfaceContainerLow = "#12233A",
        SurfaceContainer = "#182A42",
        SurfaceContainerHigh = "#213145",
        SurfaceContainerHighest = "#2A3B52",
        Background = "#0B1C30",
        OnBackground = "#EAF1FF",
        SurfaceBright = "#2A3B52",
        SurfaceDim = "#06121F",

        Outline = "#8B93A5",
        OutlineVariant = "#33445C",
        InverseSurface = "#EAF1FF",
        InverseOnSurface = "#0B1C30",
        InversePrimary = "#2151DA",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.60)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.30)",

        PrimaryFixed = "#DCE1FF",
        PrimaryFixedDim = "#B7C4FF",
        OnPrimaryFixed = "#001551",
        OnPrimaryFixedVariant = "#0039B5",
        SecondaryFixed = "#DDE1FF",
        SecondaryFixedDim = "#B8C4FF",
        OnSecondaryFixed = "#001453",
        OnSecondaryFixedVariant = "#173BAB",
        TertiaryFixed = "#E9DDFF",
        TertiaryFixedDim = "#D0BCFF",
        OnTertiaryFixed = "#23005C",
        OnTertiaryFixedVariant = "#5516BE",
    };

    /// <summary>
    /// Высокая контрастность — тёмная схема с усиленными текстом, рамками и плоскостями. Без неё Flare
    /// ставит корню приложения класс <c>flare-mode-high-contrast</c>, для которого у палитры нет правила,
    /// и внутри приложения действует светлая схема поверх тёмной страницы.
    /// </summary>
    private static readonly ColorScheme HighContrast = Dark with
    {
        Primary = "#DCE1FF",
        OnPrimary = "#000B33",
        Surface = "#000A16",
        Background = "#000A16",
        SurfaceDim = "#000000",
        SurfaceContainerLowest = "#000000",
        SurfaceContainerLow = "#06121F",
        SurfaceContainer = "#0B1C30",
        SurfaceContainerHigh = "#12233A",
        SurfaceContainerHighest = "#182A42",
        OnSurface = "#FFFFFF",
        OnBackground = "#FFFFFF",
        OnSurfaceVariant = "#EAF1FF",
        OnSurfaceVariant2 = "#C4C5D7",
        Outline = "#C4C5D7",
        OutlineVariant = "#8B93A5",
    };

    /// <summary>Единственная палитра Alva: дизайн-источник не описывает альтернативных.</summary>
    public static readonly Palette Sapphire = new()
    {
        Id = SapphireId,
        Name = "Alva Sapphire",
        Source = SourceName,
        Light = Light,
        Dark = Dark,
        HighContrast = HighContrast,
    };

    /// <summary>Все палитры темы.</summary>
    public static IReadOnlyList<Palette> All => [Sapphire];
}
