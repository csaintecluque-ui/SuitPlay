using Calculator.Models;

namespace Calculator;

/// <summary>
/// Analyse complète d'une combinaison, à la manière de SuitPlay original :
/// les lignes candidates du moteur sont rejouées par le « juge » (<see cref="LineEvaluator"/>) contre toutes les défenses,
/// puis comparées par objectif de levées, en moyenne et en matchpoints.
/// </summary>
public class SuitAnalysis
{
    public Result2 Result { get; }
    public List<(LineItem Line, Dictionary<string, (double Probability, int Tricks)> Outcomes)> Lines { get; }

    /// <summary>Levées assurées par la meilleure ligne quelle que soit la répartition.</summary>
    public int GuaranteedTricks { get; }

    /// <summary>Levées maximales possibles (au moins une répartition).</summary>
    public int MaxTricks { get; }

    /// <summary>Objectifs à afficher, du plus ambitieux au plus modeste (le dernier peut être assuré).</summary>
    public List<int> Goals { get; }

    /// <summary>Meilleure ligne en matchpoints (bat ou égale toutes les autres en face à face).</summary>
    public LineItem MatchpointBest { get; }

    private SuitAnalysis(Result2 result, List<(LineItem Line, Dictionary<string, (double Probability, int Tricks)> Outcomes)> lines)
    {
        Result = result;
        Lines = lines;
        var nonEmpty = Lines.Where(l => l.Outcomes.Count > 0).ToList();
        GuaranteedTricks = nonEmpty.Count == 0 ? 0 : nonEmpty.Max(l => l.Outcomes.Values.Min(o => o.Tricks));
        MaxTricks = nonEmpty.Count == 0 ? 0 : nonEmpty.Max(l => l.Outcomes.Values.Max(o => o.Tricks));
        Goals = Enumerable.Range(1, Math.Max(0, MaxTricks))
            .Where(g => g >= GuaranteedTricks)
            .Where(g => g <= GuaranteedTricks || nonEmpty.Any(l => LineComparison.ProbabilityAtLeast(l.Outcomes, g) > 0))
            .OrderByDescending(g => g)
            .ToList();
        MatchpointBest = nonEmpty.Count == 0 ? null : LineComparison.MatchpointBest(nonEmpty, l => l.Outcomes).First().Line;
    }

    /// <summary>Analyse une combinaison déjà calculée par le moteur.</summary>
    /// <param name="perCriterion">Nombre de lignes retenues par critère avant d'être rejouées (compromis précision/rapidité).</param>
    public static SuitAnalysis Analyse(Result2 result, int perCriterion = 40, MiniMax.MiniMaxSettings settings = null)
    {
        var roughLines = result.LineItems.Select(line => (Line: line, Outcomes: LineComparison.Outcomes(line, result))).ToList();
        var candidates = LineComparison.Candidates(roughLines, l => l.Outcomes, perCriterion);
        var judged = candidates
            .Select(c => (Line: c.Line, Outcomes: LineEvaluator.Outcomes(c.Line, result, settings)))
            .ToList();
        return new SuitAnalysis(result, judged);
    }

    public Dictionary<string, (double Probability, int Tricks)> OutcomesOf(LineItem line) =>
        Lines.FirstOrDefault(l => l.Line == line).Outcomes ?? new Dictionary<string, (double Probability, int Tricks)>();

    /// <summary>Meilleure ligne pour « au moins <paramref name="goal"/> levées » et ses chances.</summary>
    public (LineItem Line, double Probability) BestFor(int goal)
    {
        var best = Lines.Where(l => l.Outcomes.Count > 0)
            .Select(l => (l.Line, Probability: LineComparison.ProbabilityAtLeast(l.Outcomes, goal)))
            .OrderByDescending(x => x.Probability)
            .ThenByDescending(x => LineComparison.Average(OutcomesOf(x.Line)))
            .FirstOrDefault();
        return best;
    }

    /// <summary>Ligne qui fait le plus de levées en moyenne.</summary>
    public (LineItem Line, double Average) BestAverage()
    {
        return Lines.Where(l => l.Outcomes.Count > 0)
            .Select(l => (l.Line, Average: LineComparison.Average(l.Outcomes)))
            .OrderByDescending(x => x.Average)
            .FirstOrDefault();
    }

    /// <summary>Bilan en face à face de la ligne contre les autres lignes : part des duels gagnés, perdus.</summary>
    public (double Wins, double Losses) MatchpointRecord(LineItem line)
    {
        var mine = OutcomesOf(line);
        double wins = 0, losses = 0;
        var others = Lines.Where(l => l.Line != line && l.Outcomes.Count > 0).ToList();
        if (others.Count == 0)
            return (0, 0);
        foreach (var other in others)
        {
            var (a, b) = LineComparison.HeadToHead(mine, other.Outcomes);
            wins += a;
            losses += b;
        }
        return (wins / others.Count, losses / others.Count);
    }
}
