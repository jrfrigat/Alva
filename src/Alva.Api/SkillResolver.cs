using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alva.Api;

/// <summary>Resolves skill names to the team's skills, creating the missing ones once.</summary>
internal static class SkillResolver
{
    public static async Task<Guid[]> ResolveAsync(AlvaDbContext db, Guid teamId, IEnumerable<string?>? names, CancellationToken ct = default)
    {
        var wanted = (names ?? []).Select(SkillMath.Name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wanted.Length == 0) return [];
        var existing = await db.Actors.Where(a => a.TeamId == teamId && a.Kind == ActorKind.Skill).ToListAsync(ct);
        var byName = existing.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<Guid>(wanted.Length);
        foreach (var name in wanted)
        {
            if (byName.TryGetValue(name, out var id)) { result.Add(id); continue; }
            var skill = new Actor { TeamId = teamId, Kind = ActorKind.Skill, Name = name };
            db.Add(skill);
            byName[name] = skill.Id;
            result.Add(skill.Id);
        }
        return [.. result];
    }
}
