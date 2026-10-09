using Flare.Abstractions;
using Flare.Abstractions.Tokens;
using Flare.Abstractions.Tokens.Components;
using Flare.Theme.MaterialDesign3Expressive;
using Flare.Theming;

namespace Alva.Theme.Alva;

/// <summary>
/// Тема Alva: дизайн Stitch, выраженный токенами Flare.
/// </summary>
/// <remarks>
/// Тема выведена из Material 3 Expressive, а не написана с нуля: дизайн-источник сам говорит, что он
/// «grounded in the tenets of Material Design 3 Expressive». Переопределяется только то, чем дизайн
/// от Material отличается:
/// <list type="bullet">
/// <item><description><b>Цвет</b> — своей палитрой (<see cref="AlvaPalettes"/>), потому что стоковая
/// палитра MD3E и делала экран похожим на MD3E.</description></item>
/// <item><description><b>Типографика</b> — Inter и шкала дизайна: тесные строки, микро-лейблы
/// 10–11px с трекингом.</description></item>
/// <item><description><b>Форма</b> — единый радиус 6px для элементов данных вместо щедрого
/// скругления Material.</description></item>
/// <item><description><b>Глубина</b> — плоскости разделяются рамками, а не тональной высотой: тени
/// оставлены только всплывающим слоям (уровни 2–3).</description></item>
/// </list>
/// Ассеты базовой темы не отключаются (<c>inheritStyleAssets</c> не передаётся): Alva остаётся в
/// стилевой семье Material, и наши правила лишь добавляются после её таблиц — в отличие от темы
/// StitchFlow, которая была отдельной стилевой семьёй.
/// </remarks>
public static class AlvaTheme
{
    /// <summary>Стабильный id темы; он же CSS-класс, на который ограничены её правила.</summary>
    public const string ThemeId = "alva";

    /// <summary>Гарнитура интерфейса — единственная, названная дизайном.</summary>
    public const string UiFont = "Inter";

    /// <summary>Собирает тему. Регистрируется один раз, выбирается по <see cref="ThemeId"/>.</summary>
    public static ITheme Create() => new MaterialDesign3ExpressiveTheme().Derive(
        id: ThemeId,
        displayName: "Alva Sapphire",
        design: Apply,
        palettes: AlvaPalettes.All,
        defaultPaletteId: AlvaPalettes.SapphireId,
        // Гарнитура дизайна и правила, которые токены не выражают (см. таблицу темы).
        styleAssets:
        [
            "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap",
            $"_content/Alva.Theme.Alva/css/{ThemeId}.css",
        ]);

    private static DesignTokens Apply(DesignTokens d) => d with
    {
        FocusRing = "2px solid var(--flare-color-primary)",

        // Шкала дизайна: заголовки 30/22/18, тело 14/13/12, микро-лейблы 13/11/10.
        Typography = d.Typography with
        {
            DisplayLarge = Type("700", "1.875rem", "2.375rem", "-0.02em"),
            HeadlineLarge = Type("700", "1.875rem", "2.375rem", "-0.02em"),
            HeadlineMedium = Type("600", "1.375rem", "1.75rem", "-0.015em"),
            HeadlineSmall = Type("600", "1.125rem", "1.5rem", "-0.01em"),
            TitleLarge = Type("600", "1.125rem", "1.5rem", "-0.01em"),
            TitleMedium = Type("600", "0.9375rem", "1.25rem", "-0.005em"),
            TitleSmall = Type("600", "0.875rem", "1.125rem", "0"),
            BodyLarge = Type("400", "0.875rem", "1.25rem", "0"),
            BodyMedium = Type("400", "0.8125rem", "1.125rem", "0"),
            BodySmall = Type("400", "0.75rem", "1rem", "0.01em"),
            LabelLarge = Type("600", "0.8125rem", "1.125rem", "0.01em"),
            LabelMedium = Type("600", "0.6875rem", "0.875rem", "0.03em"),
            LabelSmall = Type("700", "0.625rem", "0.75rem", "0.04em"),
        },

        // Единый радиус элементов данных; капсула остаётся только для статусов и счётчиков.
        Shape = d.Shape with
        {
            None = "0px",
            ExtraSmall = "4px",
            Small = "4px",
            Medium = "6px",
            Large = "8px",
            ExtraLarge = "12px",
            Full = "9999px",
        },

        // Рамки вместо высоты: данные лежат плоскими плоскостями, тень поднимает только overlay.
        Elevation = d.Elevation with
        {
            Level0 = "none",
            Level1 = "none",
            Level2 = "0 2px 4px -1px rgba(15, 23, 42, 0.06), 0 1px 2px -1px rgba(15, 23, 42, 0.04)",
            Level3 = "0 10px 15px -3px rgba(15, 23, 42, 0.08), 0 4px 6px -4px rgba(15, 23, 42, 0.04)",
        },

        // Компоненты задаются токенами Flare, а не CSS: тема пишет их в свой стиль после
        // таблиц ассетов, и переменная из style-asset с тем же селектором ему проигрывает.

        // «Primary: ... height 32px (compact), radius 6px». Капсула MD3E заменена радиусом формы.
        Button = d.Button with
        {
            ContainerRadius = Radius,
            RadiusXs = CornerRadiusTokens.All(Radius),
            RadiusSm = CornerRadiusTokens.All(Radius),
            RadiusMd = CornerRadiusTokens.All(Radius),
            RadiusLg = CornerRadiusTokens.All(Radius),
            RadiusXl = CornerRadiusTokens.All(Radius),
            IconOnlyRadiusXs = Radius,
            IconOnlyRadiusSm = Radius,
            IconOnlyRadiusMd = Radius,
            IconOnlyRadiusLg = Radius,
            IconOnlyRadiusXl = Radius,
            HeightXs = "28px",
            HeightSm = "32px",
            HeightMd = "32px",
            HeightLg = "40px",
            HeightXl = "48px",
            IconOnlySizeXs = "28px",
            IconOnlySizeSm = "32px",
            IconOnlySizeMd = "32px",
            IconOnlySizeLg = "40px",
            IconOnlySizeXl = "48px",
            PaddingInlineXs = "10px",
            PaddingInlineSm = "12px",
            PaddingInlineMd = "12px",
            PaddingInlineLg = "16px",
            PaddingInlineXl = "20px",
            IconSizeSm = "18px",
            IconSizeMd = "18px",
            OutlinedBorderColor = "#CBD5E1",
        },

        // «Standard Inputs: 30px compact height, #FFFFFF background, 1px border #CBD5E1».
        Input = d.Input with
        {
            HeightSm = "28px",
            HeightMd = "30px",
            OutlinedRadius = Radius,
            FilledVariantRadius = Radius,
            OutlinedVariantRadius = Radius,
            BorderColor = "#CBD5E1",
            OutlinedVariantBorderColor = "#CBD5E1",
        },

        // Пункт меню макета: строка ~30px, радиус 4px; активный залит primary-container,
        // текст белый и жирный, иконка 18px.
        Nav = d.Nav with
        {
            ItemHeight = "32px",
            ItemRadius = "4px",
            IndicatorRadius = "4px",
            ActiveIndicator = "#1D4ED8",
            ActiveColor = "#FFFFFF",
            ActiveWeight = "700",
            IconSize = "18px",
        },

        // Оболочка макета: белая шапка 56px и белая боковая панель на канве #F8F9FF,
        // разделённые лёгкой тенью, а не тональной заливкой MD3.
        Layout = d.Layout with
        {
            AppBarHeight = "56px",
            AppBarBg = "#FFFFFF",
            AppBarShadow = "0 1px 8px rgba(0, 0, 0, 0.04)",
            DrawerBg = "#FFFFFF",
            RailBg = "#FFFFFF",
            ShellBg = "#F8F9FF",
            ContentPadding = "1rem 1.25rem",
        },

        // «Level 1: Pure #FFFFFF cards ... bounded by an explicit 1px outline of #E2E8F0».
        Card = d.Card with
        {
            OutlinedBg = "#FFFFFF",
            OutlinedBorder = "1px solid #E2E8F0",
            Radius = Radius,
        },

        Alert = d.Alert with { Radius = Radius },
    };

    /// <summary>Единый радиус дизайна для кнопок, полей, карточек и бейджей.</summary>
    private const string Radius = "6px";

    /// <summary>Одна ступень шкалы: гарнитура, насыщенность, размер, интерлиньяж, трекинг.</summary>
    private static TypeStyle Type(string weight, string size, string lineHeight, string spacing) => new()
    {
        FontFamily = UiFont,
        FontWeight = weight,
        FontSize = size,
        LineHeight = lineHeight,
        LetterSpacing = spacing,
    };
}
