using System.Text.Json;

namespace Alva.Contracts;

/// <summary>Portable graph. CSV/XLSX represent Rows as kind,id,payload columns.</summary>
public sealed record DataPackage(int Version, Guid SourceTeamId, DataRow[] Rows);
public sealed record DataRow(string Kind, Guid Id, JsonElement Payload);
public sealed record DataIssue(int Row, string Field, string Message);
public sealed record DataImportResult(int Imported, Dictionary<Guid, Guid> IdMap, string[] Warnings);
