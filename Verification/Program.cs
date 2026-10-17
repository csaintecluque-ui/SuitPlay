// Banc de comparaison : rejoue notre moteur sur une combinaison dont on connaît les résultats
// du SuitPlay original (fichier etalons-suitplay) et écrit les écarts sur la sortie standard.
// Usage : Verification <fichier.json> <fausses cartes true|false>
// Lignes produites : « ROW|... » (tableau des objectifs), « MP|... » (matchpoints), « STAT|... » (compteurs)
using System.Globalization;
using System.Text.Json;
using Calculator;
using Calculator.Models;

var file = args[0];
var falsecards = args.Length > 1 && bool.Parse(args[1]);
var mode = args.Length > 2 ? args[2] : "max";
var judgeSettings = new MiniMax.MiniMaxSettings { UseFalsecards = falsecards };
var pessimistic = mode == "min";
var jsonOptions = new JsonSerializerOptions { IncludeFields = true };

string Pct(double x) => (x * 100).ToString("0.0", CultureInfo.InvariantCulture);
string Num(double x) => x.ToString("0.00", CultureInfo.InvariantCulture);
void Row(string text) => Console.WriteLine("ROW|" + text);

var name = Path.GetFileNameWithoutExtension(file);
var etalonsDir = Path.GetDirectoryName(Path.GetFullPath(file));
var parts = name.Split('-');
var north = Utils.StringToCardArray(parts[0]);
var south = Utils.StringToCardArray(parts[1]);
var cardsNS = north.Concat(south).OrderDescending().ToArray();
var watch = System.Diagnostics.Stopwatch.StartNew();
Result2 result;
try
{
    // En mode « juge », les lignes sont générées sans fausses cartes ; le juge, lui, les autorise ou non
    var bestPlay = Calculate.CalculateBestPlay(north, south, new MiniMax.MiniMaxSettings { UseFalsecards = falsecards && mode != "eval" });
    result = Calculate.GetResult2(bestPlay, north, south,
        new Calculate.CalculateSettings { EtalonsDirectory = etalonsDir, MaxLines = 10000 });
}
catch (MaxLinesException)
{
    Row($"| {name} | trop de lignes | | | non comparé |");
    return;
}

var matches = 0;
var gapsTotal = 0.0;
// Probabilité de chaque main d'Est, telle que notre moteur la calcule
var probabilities = new Dictionary<string, double>();
foreach (var distribution in result.DistributionItems)
{
    var key = Utils.CardsToString(distribution.East);
    probabilities[key] = probabilities.GetValueOrDefault(key) + distribution.Probability;
}

// Résultats de l'original
var etalon = JsonSerializer.Deserialize<(Dictionary<string, List<int>> lines, List<string> combinations)>(File.ReadAllText(file), jsonOptions);
var originalLines = new List<(string Key, Dictionary<string, (double Probability, int Tricks)> Outcomes)>();
var duplicates = 0;
foreach (var line in etalon.lines)
{
    var outcomes = new Dictionary<string, (double Probability, int Tricks)>();
    for (var i = 0; i < etalon.combinations.Count && i < line.Value.Count; i++)
    {
        var c = etalon.combinations[i];
        if (!probabilities.TryGetValue(c, out var p))
            continue;
        if (!outcomes.TryAdd(c, (p, line.Value[i])))
            duplicates++;
    }
    originalLines.Add((line.Key, outcomes));
}
var coverage = etalon.combinations.Distinct().Where(probabilities.ContainsKey).Sum(c => probabilities[c]);
if (duplicates > 0)
    Row($"| {name} | doublons | {duplicates} | | entrées en double dans le fichier de référence |");

// Nos résultats
var ourLines = result.LineItems.Select(line => (Line: line, Outcomes: LineComparison.Outcomes(line, result, pessimistic))).ToList();
if (mode == "eval")
{
    // Le « juge » rejoue chaque ligne candidate contre toutes les défenses
    var candidates = LineComparison.Candidates(ourLines, l => l.Outcomes, 40);
    ourLines = candidates.Select(c => (c.Line, LineEvaluator.Outcomes(c.Line, result, judgeSettings))).ToList();
}

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
    Row($"| {name} | ≥ {goal} | {Pct(original)} % | {Pct(ours)} % | {(same ? "=" : (gap > 0 ? "+" : "") + Pct(gap))} |");
    if (!same && mode == "eval")
    {
        // Détail de notre meilleure ligne, dans la notation de l'original (mains d'Est)
        var best = ourLines.OrderByDescending(l => LineComparison.ProbabilityAtLeast(l.Outcomes, goal)).First();
        var byCombo = best.Outcomes
            .GroupBy(o => Utils.CardsToString(Utils.StringToCardArray(o.Key).ConvertToSmallCards(cardsNS)))
            .ToDictionary(g => g.Key, g => string.Join("/", g.Select(x => x.Value.Tricks).Distinct()));
        var vector = string.Join(" ", etalon.combinations.Select(c => $"{(c == "" ? "—" : c)}:{byCombo.GetValueOrDefault(c, "?")}"));
        var originalBest = originalLines.OrderByDescending(l => LineComparison.ProbabilityAtLeast(l.Outcomes, goal)).First();
        var originalVector = string.Join(" ", etalon.combinations.Select(c => $"{(c == "" ? "—" : c)}:{(originalBest.Outcomes.TryGetValue(c, out var o) ? o.Tricks : -1)}"));
        Row($"| {name} | détail ≥ {goal} | {originalBest.Key}<br>{originalVector} | {best.Line.Header}<br>{vector} | |");
    }
}
var originalAverage = originalLines.Max(l => LineComparison.Average(l.Outcomes));
var ourAverage = ourLines.Max(l => LineComparison.Average(l.Outcomes));
Row($"| {name} | moyenne | {Num(originalAverage)} | {Num(ourAverage)} | {(Math.Abs(ourAverage - originalAverage) < 0.005 ? "=" : Num(ourAverage - originalAverage))} |");
if (coverage < 0.999)
    Row($"| {name} | couverture | {Pct(coverage)} % | | répartitions non comparées |");

// Meilleure ligne en matchpoints
var originalMp = LineComparison.MatchpointBest(originalLines, l => l.Outcomes);
var ourMp = LineComparison.MatchpointBest(LineComparison.Candidates(ourLines, l => l.Outcomes), l => l.Outcomes);
var sameMp = ourMp.Any(o => originalMp.Any(s => Utils.IsSameLine(o.Line.Header, s.Key, cardsNS)));
Console.WriteLine($"MP|| {name} ({result.LineItems.Count} lignes, {watch.Elapsed.TotalSeconds:0.0} s) | {string.Join("<br>", originalMp.Select(s => s.Key))} | {string.Join("<br>", ourMp.Select(o => o.Line.Header))} | {(sameMp ? "oui" : "**non**")} |");

// Contrôle du juge : il évalue les lignes de l'original, comparées aux résultats de l'original
if (mode == "eval" && result.LineItems.Count > 0)
{
    var template = result.LineItems[0];
    var mismatches = 0;
    var checkedCount = 0;
    var examples = new List<string>();
    foreach (var (key, tricksList) in etalon.lines)
    {
        var sequences = key.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => new Cards(x)).ToList();
        if (sequences.Count == 0) continue;
        var pseudoLine = new LineItem { Line = sequences, Items2 = template.Items2 };
        var judged = LineEvaluator.Outcomes(pseudoLine, result, judgeSettings);
        // Regroupe par main d'Est en petites cartes (notation de l'original)
        var judgedByCombo = judged
            .GroupBy(j => Utils.CardsToString(Utils.StringToCardArray(j.Key).ConvertToSmallCards(cardsNS)))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value.Tricks).Distinct().OrderBy(x => x).ToList());
        for (var i = 0; i < etalon.combinations.Count && i < tricksList.Count; i++)
        {
            if (!judgedByCombo.TryGetValue(etalon.combinations[i], out var ours)) continue;
            checkedCount++;
            if (ours.Count == 1 && ours[0] == tricksList[i]) continue;
            mismatches++;
            if (examples.Count < 6)
                examples.Add($"{key} / Est {(etalon.combinations[i] == "" ? "—" : etalon.combinations[i])} : original {tricksList[i]}, juge {string.Join(",", ours)}");
        }
    }
    Console.WriteLine($"CHK|| {name} | {checkedCount} | {mismatches} | {string.Join("<br>", examples)} |");
}

Console.WriteLine($"STAT|{matches}|{gapsTotal.ToString(CultureInfo.InvariantCulture)}|{(sameMp ? 1 : 0)}");
