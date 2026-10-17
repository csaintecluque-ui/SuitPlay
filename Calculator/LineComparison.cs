using Calculator.Models;

namespace Calculator;

/// <summary>
/// Comparaisons de lignes de jeu, comme dans SuitPlay original :
/// - chances d'atteindre un objectif de levées,
/// - meilleure ligne en matchpoints (« mp-best ») : celle qui gagne plus souvent qu'elle ne perd contre chacune des autres.
/// </summary>
public static class LineComparison
{
    /// <summary>Résultat d'une ligne pour chaque répartition adverse (clé = main d'Est en petites cartes).</summary>
    public static Dictionary<string, (double Probability, int Tricks)> Outcomes(LineItem line, Result2 result)
    {
        var cardsNS = result.North.Concat(result.South).OrderDescending().ToArray();
        var probabilities = new Dictionary<string, double>();
        foreach (var distribution in result.DistributionItems)
        {
            var key = Utils.CardsToString(distribution.East);
            probabilities[key] = probabilities.GetValueOrDefault(key) + distribution.Probability;
        }

        var outcomes = new Dictionary<string, (double, int)>();
        foreach (var item in line.Items2)
        {
            if (item.Tricks.First() == -1)
                continue;
            var key = Utils.CardsToString(item.Combination.ConvertToSmallCards(cardsNS));
            if (!probabilities.TryGetValue(key, out var probability))
                continue;
            var tricks = item.Tricks.Max();
            // Plusieurs combinaisons peuvent donner la même clé : on garde le résultat le plus prudent
            outcomes[key] = outcomes.TryGetValue(key, out var existing)
                ? (probability, Math.Min(existing.Item2, tricks))
                : (probability, tricks);
        }
        return outcomes;
    }

    /// <summary>Probabilité que la ligne A fasse plus de levées que la ligne B, et l'inverse.</summary>
    public static (double AWins, double BWins) HeadToHead(
        Dictionary<string, (double Probability, int Tricks)> a,
        Dictionary<string, (double Probability, int Tricks)> b)
    {
        double aWins = 0, bWins = 0;
        foreach (var (key, outcomeA) in a)
        {
            if (!b.TryGetValue(key, out var outcomeB))
                continue;
            if (outcomeA.Tricks > outcomeB.Tricks) aWins += outcomeA.Probability;
            else if (outcomeB.Tricks > outcomeA.Tricks) bWins += outcomeA.Probability;
        }
        return (aWins, bWins);
    }

    /// <summary>
    /// Meilleure(s) ligne(s) en matchpoints : ne perd(ent) contre aucune autre ligne en face à face.
    /// S'il n'y en a pas, renvoie la ligne au meilleur bilan cumulé.
    /// </summary>
    public static List<T> MatchpointBest<T>(IReadOnlyList<T> lines, Func<T, Dictionary<string, (double Probability, int Tricks)>> outcomesOf)
    {
        const double tolerance = 1e-9;
        var outcomes = lines.Select(outcomesOf).ToList();
        var unbeaten = new List<int>();
        var balance = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var beaten = false;
            for (var j = 0; j < lines.Count; j++)
            {
                if (i == j) continue;
                var (iWins, jWins) = HeadToHead(outcomes[i], outcomes[j]);
                balance[i] += iWins - jWins;
                if (jWins > iWins + tolerance)
                    beaten = true;
            }
            if (!beaten)
                unbeaten.Add(i);
        }

        if (unbeaten.Count > 0)
            return unbeaten.Select(i => lines[i]).ToList();
        var best = balance.Max();
        return Enumerable.Range(0, lines.Count).Where(i => Math.Abs(balance[i] - best) < tolerance).Select(i => lines[i]).ToList();
    }

    /// <summary>Chances d'au moins <paramref name="goal"/> levées.</summary>
    public static double ProbabilityAtLeast(Dictionary<string, (double Probability, int Tricks)> outcomes, int goal) =>
        outcomes.Values.Where(x => x.Tricks >= goal).Sum(x => x.Probability);

    /// <summary>Nombre moyen de levées.</summary>
    public static double Average(Dictionary<string, (double Probability, int Tricks)> outcomes)
    {
        var total = outcomes.Values.Sum(x => x.Probability);
        return total <= 0 ? 0 : outcomes.Values.Sum(x => x.Probability * x.Tricks) / total;
    }
}
