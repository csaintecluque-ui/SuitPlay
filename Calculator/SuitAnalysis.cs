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
        // Le juge laisse toujours la défense fournir de fausses cartes, comme l'original (mesuré : plus proche)
        var judgeSettings = new MiniMax.MiniMaxSettings { UseFalsecards = true, EntriesNorth = settings?.EntriesNorth, EntriesSouth = settings?.EntriesSouth };
        var roughLines = result.LineItems.Select(line => (Line: line, Outcomes: LineComparison.Outcomes(line, result))).ToList();
        var candidates = LineComparison.Candidates(roughLines, l => l.Outcomes, perCriterion);
        var judged = candidates
            .Select(c => (Line: c.Line, Outcomes: LineEvaluator.Outcomes(c.Line, result, judgeSettings)))
            .ToList();
        return new SuitAnalysis(result, judged) { JudgeSettings = judgeSettings };
    }

    private MiniMax.MiniMaxSettings JudgeSettings { get; init; }

    /// <summary>Déroulement de la ligne contre une main d'Est donnée (clé = main d'Est réelle, comme dans <see cref="OutcomesOf"/>).</summary>
    public List<Face> PlayOf(LineItem line, string eastKey) =>
        LineEvaluator.PlayFor(line, Result, Utils.StringToCardArray(eastKey), JudgeSettings);

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

    /// <summary>Score moyen (en points de duplicate) de la ligne pour ce contrat.</summary>
    public double ExpectedScore(LineItem line, ImpContract contract)
    {
        var outcomes = OutcomesOf(line);
        var total = outcomes.Values.Sum(o => o.Probability);
        return total <= 0 ? 0 : outcomes.Values.Sum(o => o.Probability * contract.Score(o.Tricks)) / total;
    }

    /// <summary>Gain moyen en IMP de la ligne <paramref name="a"/> face à la ligne <paramref name="b"/>.</summary>
    public double ExpectedImps(LineItem a, LineItem b, ImpContract contract)
    {
        var outcomesA = OutcomesOf(a);
        var outcomesB = OutcomesOf(b);
        double imps = 0, total = 0;
        foreach (var (key, outcomeA) in outcomesA)
        {
            if (!outcomesB.TryGetValue(key, out var outcomeB))
                continue;
            imps += outcomeA.Probability * ImpScoring.ToImps(contract.Score(outcomeA.Tricks) - contract.Score(outcomeB.Tricks));
            total += outcomeA.Probability;
        }
        return total <= 0 ? 0 : imps / total;
    }

    /// <summary>
    /// Meilleure ligne en IMP (« imp-best ») : son gain moyen en IMP est positif ou nul face à chacune des autres lignes.
    /// S'il n'y en a pas, la ligne au meilleur gain cumulé. Renvoie aussi son gain moyen face à la meilleure autre ligne.
    /// </summary>
    public (LineItem Line, double WorstNetImps) ImpBest(ImpContract contract)
    {
        const double tolerance = 1e-9;
        var lines = Lines.Where(l => l.Outcomes.Count > 0).Select(l => l.Line).ToList();
        if (lines.Count == 0)
            return (null, 0);
        if (lines.Count == 1)
            return (lines[0], 0);

        var n = lines.Count;
        var net = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                net[i, j] = ExpectedImps(lines[i], lines[j], contract);
                net[j, i] = -net[i, j];
            }

        var sums = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, n).Where(j => j != i).Sum(j => net[i, j])).ToArray();
        var unbeaten = Enumerable.Range(0, n).Where(i => Enumerable.Range(0, n).All(j => j == i || net[i, j] >= -tolerance)).ToList();
        var pool = unbeaten.Count > 0 ? unbeaten : Enumerable.Range(0, n).ToList();
        var best = pool.OrderByDescending(i => sums[i]).First();
        var worst = Enumerable.Range(0, n).Where(j => j != best).Min(j => net[best, j]);
        return (lines[best], worst);
    }
}
