using System.Text;

namespace Alva.Contracts;

/// <summary>Normalizes skill tags and compares task requirements with a person's skills.</summary>
public static class SkillMath
{
    public const int MaxTags = 50;
    public const int MaxLength = 80;

    public static string[] Normalize(IEnumerable<string?>? tags)
    {
        var values = (tags ?? []).Select(t => string.Join(" ", (t ?? "").Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length > MaxTags || values.Any(t => t.Length > MaxLength || t.Any(char.IsControl)))
            throw new ArgumentException("Skills: at most 50 tags, each at most 80 characters.");
        return values;
    }

    public static string[] Missing(IEnumerable<string?>? required, IEnumerable<string?>? skills) =>
        Normalize(required).Except(Normalize(skills), StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool Compatible(IEnumerable<string?>? required, IEnumerable<string?>? skills) => Missing(required, skills).Length == 0;
}
