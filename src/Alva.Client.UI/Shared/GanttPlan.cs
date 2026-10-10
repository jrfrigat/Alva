namespace Alva.Client.Shared;

/// <summary>Что перетаскивают на доске планирования: задачу или человека.</summary>
public abstract record PlanDrag;

/// <summary>Задача из бэклога или со строки Гантта.</summary>
public sealed record TaskDrag(Guid TaskId) : PlanDrag;

/// <summary>Человек из блока «Люди спринта».</summary>
public sealed record PersonDrag(Guid PersonId) : PlanDrag;
