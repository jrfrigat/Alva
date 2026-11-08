using Alva.Contracts;
using Google.OrTools.Sat;

namespace Alva.Api;

/// <summary>
/// Sprint auto-balance on CP-SAT (Google OR-Tools). Each task gets exactly one "person + start day" option
/// from those prepared by <see cref="BalanceMath.Build"/>; the objective is the same weighted sum computed by <see cref="BalanceMath.Evaluate"/>,
/// in whole tenths of an hour. The current plan is given to the solver as a hint, so the answer is no worse than the original.
/// </summary>
public static class BalanceSolver
{
    public static (int[] Choice, string Status) Solve(BalanceProblem problem, TimeSpan limit)
    {
        if (problem.Tasks.Length == 0) return ([], BalanceMath.Optimal);
        // An infeasibility found while building (fixed dates versus links) cannot be resolved by the model.
        if (problem.Infeasible is not null) return ([], BalanceMath.Infeasible);
        var model = new CpModel();
        var w = problem.Weights;
        var z = problem.Tasks.Select((t, ti) => t.Options.Select((_, oi) => model.NewBoolVar($"z{ti}_{oi}")).ToArray()).ToArray();
        foreach (var options in z) model.AddExactlyOne(options);

        var objective = LinearExpr.NewBuilder();
        var maxLoad = problem.Tasks.Sum(t => (long)t.Tenths) + problem.Background.Max(row => row.DefaultIfEmpty(0).Max()) + 1;

        // Overload by person and day: over >= background + load - capacity.
        var overByPerson = new LinearExpr[problem.People.Length];
        var freeByPerson = new LinearExpr[problem.People.Length];
        for (var p = 0; p < problem.People.Length; p++)
        {
            var personOver = LinearExpr.NewBuilder();
            var personLoad = LinearExpr.NewBuilder();
            for (var d = 0; d < problem.Horizon; d++)
            {
                var load = LinearExpr.NewBuilder();
                var any = false;
                for (var t = 0; t < problem.Tasks.Length; t++)
                for (var o = 0; o < problem.Tasks[t].Options.Length; o++)
                {
                    var option = problem.Tasks[t].Options[o];
                    if (option.Person != p || option.Load[d] == 0) continue;
                    load.AddTerm(z[t][o], option.Load[d]);
                    if (d < problem.SprintDays) personLoad.AddTerm(z[t][o], option.Load[d]);
                    any = true;
                }
                var fixedOver = problem.Background[p][d] - problem.Capacity[p][d];
                if (!any)
                {
                    if (fixedOver > 0) personOver.Add(fixedOver);
                    continue;
                }
                var over = model.NewIntVar(0, maxLoad, $"over{p}_{d}");
                model.Add(over >= load + fixedOver);
                personOver.Add(over);
            }
            overByPerson[p] = personOver;
            var fixedFree = Enumerable.Range(0, problem.SprintDays).Sum(d => (long)problem.Capacity[p][d] - problem.Background[p][d]);
            freeByPerson[p] = LinearExpr.Affine(personLoad, -1, fixedFree);
            objective.AddTerm(overByPerson[p], w.Overload);
        }

        if (w.Peak > 0 && problem.People.Length > 0)
        {
            var peak = model.NewIntVar(0, maxLoad * problem.Horizon, "peak");
            foreach (var over in overByPerson) model.Add(peak >= over);
            objective.AddTerm(peak, w.Peak);
        }

        // Deadlines, overflow past the sprint, unassigned people, and changes are constants of the option.
        for (var t = 0; t < problem.Tasks.Length; t++)
        for (var o = 0; o < problem.Tasks[t].Options.Length; o++)
        {
            var option = problem.Tasks[t].Options[o];
            var cost = w.LateDay * option.LateDays * problem.Tasks[t].Weight + w.Overflow * option.OverflowTenths
                + (option.Person < 0 ? w.Unassigned * problem.Tasks[t].Tenths : 0) + (o == 0 ? 0 : w.Change);
            if (cost != 0) objective.AddTerm(z[t][o], cost);
        }

        // Links are hard: a plan that violates FS/SS/FF/SF with the lag taken into account is inadmissible.
        {
            LinearExpr Start(int t) => LinearExpr.WeightedSum(z[t], problem.Tasks[t].Options.Select(o => (long)o.Start));
            // End is the actual end, with the tail of work that the daily limit pushed outside the window.
            LinearExpr End(int t) => LinearExpr.WeightedSum(z[t], problem.Tasks[t].Options.Select(o => (long)(o.Start + o.Span)));
            foreach (var link in problem.Links)
            {
                var gap = link.Kind switch
                {
                    "SS" => Start(link.From) + link.Lag - Start(link.To),
                    "FF" => End(link.From) + link.Lag - End(link.To),
                    "SF" => Start(link.From) + link.Lag - End(link.To),
                    _ => End(link.From) + link.Lag - Start(link.To)
                };
                model.Add(gap <= 0);
            }
        }

        // Spread of free hours within departments.
        if (w.Spread > 0)
            for (var g = 0; g < problem.Groups.Length; g++)
            {
                var bound = maxLoad * problem.Horizon;
                var high = model.NewIntVar(-bound, bound, $"high{g}");
                var low = model.NewIntVar(-bound, bound, $"low{g}");
                foreach (var p in problem.Groups[g])
                {
                    model.Add(high >= freeByPerson[p]);
                    model.Add(low <= freeByPerson[p]);
                }
                objective.AddTerm(high - low, w.Spread);
            }

        model.Minimize(objective);
        for (var t = 0; t < z.Length; t++)
        for (var o = 0; o < z[t].Length; o++)
            model.AddHint(z[t][o], o == 0);

        var solver = new CpSolver
        {
            StringParameters = $"max_time_in_seconds:{limit.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} num_workers:8"
        };
        var status = solver.Solve(model);
        // Contradictory constraints get an explicit answer: falling back to a plan that violates links is not allowed.
        if (status == CpSolverStatus.Infeasible) return ([], BalanceMath.Infeasible);
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return (BalanceMath.Greedy(problem), BalanceMath.Heuristic);
        var choice = z.Select(options => Array.FindIndex(options, v => solver.BooleanValue(v))).Select(i => Math.Max(0, i)).ToArray();
        return (choice, status == CpSolverStatus.Optimal ? BalanceMath.Optimal : BalanceMath.Feasible);
    }
}
