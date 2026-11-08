using System.Text;

namespace Alva.Contracts;

/// <summary>Normalizes skill tags and compares task requirements with a person's skills.</summary>
public static class SkillMath
{
    public const int MaxTags = 50;
    public const int MaxLength = 80;

    // The server folds full-width compatibility characters with FormKC. The ICU build used by
    // WebAssembly supports only FormC, so the browser keeps the text intact instead of crashing.
    private static readonly NormalizationForm Normalization =
        OperatingSystem.IsBrowser() ? NormalizationForm.FormC : NormalizationForm.FormKC;

    public static string[] Normalize(IEnumerable<string?>? tags)
    {
        var values = (tags ?? []).Select(NormalizeTag)
            .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length > MaxTags || values.Any(t => t.Length > MaxLength || t.Any(char.IsControl)))
            throw new ArgumentException("Skills: at most 50 tags, each at most 80 characters.");
        return values;
    }

    private static string NormalizeTag(string? tag)
    {
        var text = tag ?? "";
        try { text = text.Normalize(Normalization); }
        catch (PlatformNotSupportedException) { /* a browser ICU build without normalization */ }
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string[] Missing(IEnumerable<string?>? required, IEnumerable<string?>? skills) =>
        Normalize(required).Except(Normalize(skills), StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool Compatible(IEnumerable<string?>? required, IEnumerable<string?>? skills) => Missing(required, skills).Length == 0;
}
