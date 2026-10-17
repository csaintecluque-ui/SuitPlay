// Banc de comparaison : rejoue notre moteur sur les combinaisons dont on connaît les résultats
// du SuitPlay original (fichiers etalons-suitplay) et mesure les écarts.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Calculator;
using Calculator.Models;

var etalonsDir = args.Length > 0 ? args[0] : "Calculator/etalons-suitplay";
var outputFile = args.Length > 1 ? args[1] : "rapport.md";
var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
var report = new StringBuilder();
var summary = new StringBuilder();

string Pct(double x) => (x * 100).ToString("0.0", CultureInfo.InvariantCulture);
string Num(double x) => x.ToString("0.00", CultureInfo.InvariantCulture);

report.AppendLine("# Notre moteur comparé au SuitPlay original");
report.AppendLine();
report.AppendLine($"Généré le {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Pourcentages = meilleures chances d'atteindre l'objectif.");

foreach (var falsecards in new[] { false, true })
{
    var combos = 0;
    var matches = 0;
    var gapsTotal = 0.0;
    var mpSame = 0;
    report.AppendLine();
    report.AppendLine($"## Fausses cartes des adversaires : {(falsecards ? "oui" : "non")}");
    report.AppendLine();
    report.AppendLine("| Combinaison | Objectif | Original | Nous | Écart |");
    report.AppendLine("|---|---|---|---|---|");
    var mpLines = new StringBuilder();

    foreach (var file in Directory.GetFiles(etalonsDir, "*.json").OrderBy(x => x))
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var parts = name.Split('-');
        var north = Utils.StringToCardArray(parts[0]);
        var south = Utils.StringToCardArray(parts[1]);
        var cardsNS = north.Concat(south).OrderDescending().ToArray();
        Result2 result;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} {name} (fausses cartes {falsecards})");
        try
        {
            var bestPlay = Calculate.CalculateBestPlay(north, south, new MiniMax.MiniMaxSettings { UseFalsecards = falsecards });
            result = Calculate.GetResult2(bestPlay, north, south,
                new Calculate.CalculateSettings { EtalonsDirectory = etalonsDir, MaxLines = 10000 });
        }
        catch (MaxLinesException)
        {
            report.AppendLine($"| {name} | trop de lignes | | | non comparé |");
            continue;
        }
        catch (Exception ex)
        {
            report.AppendLine($"| {name} | erreur | | | {ex.GetType().Name} |");
            continue;
        }
        combos++;

        // Probabilité de chaque main d'Est, telle que notre moteur la calcule
        var probabilities = new Dictionary<string, double>();
        foreach (var distribution in result.DistributionItems)
        {
            var key = Utils.CardsToString(distribution.East);
            probabilities[key] = probabilities.GetValueOrDefault(key) + distribution.Probability;
        }

        // Résultats de l'original
        var etalon = JsonSerializer.Deserialize<(Dictionary<string, List<int>> lines, List<string> combinations)>(File.ReadAllText(file), jsonOptions);
        var originalLines = etalon.lines.Select(line => (Key: line.Key, Outcomes: etalon.combinations
                .Select((c, i) => (c, i))
                .Where(x => probabilities.ContainsKey(x.c))
                .ToDictionary(x => x.c, x => (Probability: probabilities[x.c], Tricks: line.Value[x.i]))))
            .ToList();
        var coverage = etalon.combinations.Where(probabilities.ContainsKey).Sum(c => probabilities[c]);

        // Nos résultats
        var ourLines = result.LineItems.Select(line => (Line: line, Outcomes: LineComparison.Outcomes(line, result))).ToList();

        var maxTricks = Math.Max(
            originalLines.Max(l => l.Outcomes.Values.Max(o => o.Tricks)),
            ourLines.Where(l => l.Outcomes.Count > 0).Max(l => l.Outcomes.Values.Max(o => o.Tricks)));
        for (var goal = maxTricks; goal >= 1; goal--)
        {
            var original = originalLines.Max(l => LineComparison.ProbabilityAtLeast(l.Outcomes, goal));
            var ours = ourLines.Max(l => LineComparison.ProbabilityAtLeast(l.Outcomes, goal));
            if (original >= 0.9999 && ours >= 0.9999)
                continue;
            var gap = ours - original;
            gapsTotal += Math.Abs(gap);
            var same = Math.Abs(gap) < 0.0005;
            if (same) matches++;
            report.AppendLine($"| {name} | ≥ {goal} | {Pct(original)} % | {Pct(ours)} % | {(same ? "=" : (gap > 0 ? "+" : "") + Pct(gap))} |");
        }
        var originalAverage = originalLines.Max(l => LineComparison.Average(l.Outcomes));
        var ourAverage = ourLines.Max(l => LineComparison.Average(l.Outcomes));
        report.AppendLine($"| {name} | moyenne | {Num(originalAverage)} | {Num(ourAverage)} | {(Math.Abs(ourAverage - originalAverage) < 0.005 ? "=" : Num(ourAverage - originalAverage))} |");
        if (coverage < 0.999)
            report.AppendLine($"| {name} | couverture | {Pct(coverage)} % | | répartitions non comparées |");

        // Meilleure ligne en matchpoints
        var originalMp = LineComparison.MatchpointBest(originalLines, l => l.Outcomes);
        var ourMp = LineComparison.MatchpointBest(LineComparison.Candidates(ourLines, l => l.Outcomes), l => l.Outcomes);
        var sameMp = ourMp.Any(o => originalMp.Any(s => Utils.IsSameLine(o.Line.Header, s.Key, cardsNS)));
        if (sameMp) mpSame++;
        Console.WriteLine($"   {result.LineItems.Count} lignes, {watch.Elapsed.TotalSeconds:0.0} s");
        mpLines.AppendLine($"| {name} ({result.LineItems.Count} lignes, {watch.Elapsed.TotalSeconds:0.0} s) | {string.Join("<br>", originalMp.Select(s => s.Key))} | {string.Join("<br>", ourMp.Select(o => o.Line.Header))} | {(sameMp ? "oui" : "**non**")} |");
    }

    report.AppendLine();
    report.AppendLine("### Meilleure ligne en matchpoints");
    report.AppendLine();
    report.AppendLine("| Combinaison | Original | Nous | Identique |");
    report.AppendLine("|---|---|---|---|");
    report.Append(mpLines);

    summary.AppendLine($"Fausses cartes {(falsecards ? "oui" : "non")} : {combos} combinaisons, {matches} objectifs identiques, écart cumulé {Pct(gapsTotal)} points, mp-best identique {mpSame}/{combos}");
}

report.Insert(0, "");
var text = report.ToString().Replace("## Fausses cartes des adversaires : non", "## Résumé\n\n" + summary.ToString().Replace("\n", "\n\n") + "\n## Fausses cartes des adversaires : non");
File.WriteAllText(outputFile, text);
Console.WriteLine(summary.ToString());
