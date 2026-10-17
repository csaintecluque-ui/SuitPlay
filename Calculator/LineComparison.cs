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
    /// <param name="pessimistic">
    /// Quand plusieurs déroulements restent possibles pour une même répartition (choix de la défense),
    /// true retient le moins bon pour le déclarant (la défense choisit), false le meilleur.
    /// </param>
    public static Dictionary<string, (double Probability, int Tricks)> Outcomes(LineItem line, Result2 result, bool pessimistic = false)
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
            var tricks = pessimistic ? item.Tricks.Min() : item.Tricks.Max();
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
        if (lines.Count <= 1)
            return lines.ToList();

        // Tableaux alignés sur les mêmes répartitions, pour comparer vite
        var outcomes = lines.Select(outcomesOf).ToList();
        var keys = outcomes.SelectMany(o => o.Keys).Distinct().ToArray();
        var probability = keys.Select(k => outcomes.Select(o => o.TryGetValue(k, out var v) ? v.Probability : 0).Max()).ToArray();
        var tricks = outcomes.Select(o => keys.Select(k => o.TryGetValue(k, out var v) ? v.Tricks : -1).ToArray()).ToArray();

        var unbeaten = new List<int>();
        var balance = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var beaten = false;
            for (var j = 0; j < lines.Count; j++)
            {
                if (i == j) continue;
                double iWins = 0, jWins = 0;
                var ti = tricks[i];
                var tj = tricks[j];
                for (var k = 0; k < keys.Length; k++)
                {
                    if (ti[k] < 0 || tj[k] < 0) continue;
                    if (ti[k] > tj[k]) iWins += probability[k];
                    else if (tj[k] > ti[k]) jWins += probability[k];
                }
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

    /// <summary>
    /// Lignes candidates pour la comparaison en matchpoints : les meilleures pour chaque objectif et en moyenne.
    /// Évite de comparer des milliers de lignes manifestement inférieures.
    /// </summary>
    public static List<T> Candidates<T>(IReadOnlyList<T> lines, Func<T, Dictionary<string, (double Probability, int Tricks)>> outcomesOf, int perCriterion = 15)
    {
        var withOutcomes = lines.Select(l => (Line: l, Outcomes: outcomesOf(l))).Where(x => x.Outcomes.Count > 0).ToList();
        if (withOutcomes.Count == 0)
            return [];
        var maxTricks = withOutcomes.Max(x => x.Outcomes.Values.Max(o => o.Tricks));
        var selected = new HashSet<int>();
        void Keep(Func<Dictionary<string, (double Probability, int Tricks)>, double> score)
        {
            foreach (var index in Enumerable.Range(0, withOutcomes.Count).OrderByDescending(i => score(withOutcomes[i].Outcomes)).Take(perCriterion))
                selected.Add(index);
        }
        Keep(Average);
        for (var goal = 1; goal <= maxTricks; goal++)
        {
            var g = goal;
            Keep(o => ProbabilityAtLeast(o, g));
        }
        return selected.OrderBy(i => i).Select(i => withOutcomes[i].Line).ToList();
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
