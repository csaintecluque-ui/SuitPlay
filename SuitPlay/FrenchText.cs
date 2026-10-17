using System.Text;
using Calculator;
using Calculator.Models;

namespace SuitPlay;

/// <summary>
/// Textes en français : noms des cartes, choix de la meilleure ligne selon l'objectif
/// et description pas à pas d'une ligne de jeu.
/// </summary>
public static class FrenchText
{
    /// <summary>Nom d'une carte avec son article : « l'As », « la Dame », « le 7 ».</summary>
    public static string CardName(Face face) => face switch
    {
        Face.Ace => "l'As",
        Face.King => "le Roi",
        Face.Queen => "la Dame",
        Face.Jack => "le Valet",
        Face.Ten => "le 10",
        Face.SmallCard => "une petite carte",
        Face.Dummy => "rien",
        _ => "le " + Utils.CardToChar(face)
    };

    /// <summary>Symbole court à la française : A R D V 10 9 … x.</summary>
    public static string ShortName(Face face) => face switch
    {
        Face.Ace => "A",
        Face.King => "R",
        Face.Queen => "D",
        Face.Jack => "V",
        Face.Ten => "10",
        Face.SmallCard => "x",
        Face.Dummy => "–",
        _ => Utils.CardToChar(face).ToString()
    };

    public static string HandToString(IEnumerable<Face> cards)
    {
        var list = cards.OrderDescending().Select(ShortName).ToList();
        return list.Count == 0 ? "(chicane)" : string.Join(" ", list);
    }

    /// <summary>Séquence de jeu lisible, levée par levée : « A x 3 x | 9 x 4 ».</summary>
    public static string SequenceToString(Cards line)
    {
        return string.Join("  |  ", line.Chunk(4).Select(trick => string.Join(" ", trick.Select(ShortName))));
    }

    /// <summary>Meilleure ligne pour un objectif : null = maximum de levées en moyenne, sinon « au moins N levées ».</summary>
    public static LineItem BestLineFor(IReadOnlyCollection<LineItem> lineItems, int? goal)
    {
        if (lineItems == null || lineItems.Count == 0)
            return null;
        if (goal == null)
            return lineItems.OrderByDescending(x => x.Average.Value).First();
        return lineItems.OrderByDescending(x => ProbabilityFor(x, goal.Value)).First();
    }

    public static double ProbabilityFor(LineItem lineItem, int goal)
    {
        var probability = lineItem.Probabilities?.FirstOrDefault(p => p.Tricks == goal);
        return probability?.Value ?? 0.0;
    }

    /// <summary>Explique la ligne de jeu, levée par levée, en supposant que les adversaires fournissent petit sauf indication.</summary>
    public static string DescribeLine(Cards line, Face[] north, Face[] south, Face[] discarded = null)
    {
        if (line == null || line.Count() == 0)
            return "Aucune ligne de jeu à afficher.";

        var played = new HashSet<Face>(discarded ?? []); // les défausses ne sont pas chez les adversaires
        var text = new StringBuilder();
        var number = 1;
        foreach (var trick in line.Chunk(4))
        {
            var sentence = DescribeTrick(trick, north, south, played);
            if (sentence == null)
                break;
            sentence += FourthHandNote(trick, north);
            text.AppendLine($"{number}. {sentence}");
            number++;
            foreach (var card in trick.Where(c => c != Face.SmallCard && c != Face.Dummy))
                played.Add(card);
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>« de l'As », « du Roi », « de la Dame »…</summary>
    private static string OfCard(Face face) => face switch
    {
        Face.Ace => "de l'As",
        Face.Queen => "de la Dame",
        _ => "du " + CardName(face)[3..]
    };

    /// <summary>Précise ce que fait la 4e main quand elle ne fournit pas une petite carte.</summary>
    private static string FourthHandNote(Face[] trick, Face[] north)
    {
        if (trick.Length < 4 || trick[3] == Face.SmallCard || trick[3] == Face.Dummy)
            return "";
        var fourthHand = north.Contains(trick[0]) ? "Ouest" : "Est";
        var wins = trick[3] > trick[0] && trick[3] > trick[2] && (trick[1] == Face.SmallCard || trick[1] == Face.Dummy || trick[3] > trick[1]);
        return wins
            ? $" {fourthHand} prend avec {CardName(trick[3])}."
            : $" {fourthHand} fournit {CardName(trick[3])}.";
    }

    private static readonly Face[] AllFaces =
        [Face.Two, Face.Three, Face.Four, Face.Five, Face.Six, Face.Seven, Face.Eight, Face.Nine, Face.Ten, Face.Jack, Face.Queen, Face.King, Face.Ace];

    /// <summary>Cartes adverses plus hautes que <paramref name="card"/> et pas encore jouées.</summary>
    private static List<Face> MissingAbove(Face card, Face[] north, Face[] south, HashSet<Face> played)
    {
        return AllFaces.Where(f => f > card && !north.Contains(f) && !south.Contains(f) && !played.Contains(f))
            .OrderDescending().ToList();
    }

    private static string DescribeTrick(Face[] trick, Face[] north, Face[] south, HashSet<Face> played)
    {
        if (trick.Length == 0)
            return null;

        var lead = trick[0];
        if (!north.Contains(lead) && !south.Contains(lead))
            return "Les adversaires ont pris la main : reprenez la main et continuez la couleur.";

        var leadHand = north.Contains(lead) ? "Nord" : "Sud";
        var otherHand = leadHand == "Nord" ? "Sud" : "Nord";
        var otherCards = leadHand == "Nord" ? south : north;
        var secondHand = leadHand == "Nord" ? "Est" : "Ouest";

        if (trick.Length < 3)
            return $"Jouez {CardName(lead)} de {leadHand}.";

        var second = trick[1];
        var partner = trick[2];

        if (partner == Face.Dummy)
            return $"Jouez {CardName(lead)} de {leadHand} ({otherHand} n'a plus de carte dans la couleur).";

        var leadIsMaster = MissingAbove(lead, north, south, played).Count == 0;
        var partnerIsMaster = MissingAbove(partner, north, south, played).Count == 0;

        if (second != Face.SmallCard && second != Face.Dummy && !leadIsMaster)
            return $"Jouez {CardName(lead)} de {leadHand} vers {otherHand} ; si {secondHand} fournit {CardName(second)}, mettez {CardName(partner)}.";

        if (leadIsMaster)
            return $"Tirez {CardName(lead)} de {leadHand}, maître (fournissez {CardName(partner)} de {otherHand}).";

        if (partnerIsMaster)
            return $"Jouez {CardName(lead)} de {leadHand} vers {otherHand} et prenez avec {CardName(partner)}.";

        if (lead < partner)
            return $"Jouez {CardName(lead)} de {leadHand} vers {otherHand} ; si {secondHand} fournit petit, mettez {CardName(partner)}.";

        // La carte d'entame est plus haute que celle fournie par le partenaire
        var partnerCanOvertake = otherCards.Any(c => c > lead && !played.Contains(c));
        if (partnerCanOvertake)
            return $"Avancez {CardName(lead)} de {leadHand} ; si {secondHand} fournit petit, laissez courir (fournissez {CardName(partner)} de {otherHand}).";

        var target = MissingAbove(lead, north, south, played).First();
        if (lead < Face.Ten)
            return $"Rejouez {CardName(lead)} de {leadHand} et concédez une levée pour affranchir la couleur (fournissez {CardName(partner)} de {otherHand}).";
        return $"Avancez {CardName(lead)} de {leadHand} pour chasser {CardName(target)} (fournissez {CardName(partner)} de {otherHand}).";
    }

    /// <summary>
    /// Variantes de la ligne : que faire quand un adversaire ne fournit pas comme dans la ligne principale
    /// (il fournit un honneur, ou n'a plus de carte dans la couleur). Triées de la plus fréquente à la plus rare.
    /// </summary>
    public static List<string> DescribeVariants(LineItem line, Result2 result, int maxVariants = int.MaxValue)
    {
        var variants = new List<string>();
        if (line == null || result == null)
            return variants;
        var north = result.North;
        var south = result.South;
        var cardsNS = north.Concat(south).OrderDescending().ToArray();
        var main = line.LongestLineIncludingGenerated;

        var probabilities = new Dictionary<string, double>();
        foreach (var distribution in result.DistributionItems)
        {
            var key = Utils.CardsToString(distribution.East);
            probabilities[key] = probabilities.GetValueOrDefault(key) + distribution.Probability;
        }

        // Regroupe les jeux réels par « premier écart » avec la ligne principale
        var groups = new Dictionary<(int Index, Face Card), (double Probability, double BestProbability, Cards Play, HashSet<string> Distributions)>();
        foreach (var item2 in line.Items2)
        {
            if (item2.Items == null || item2.Items.Count == 0)
                continue;
            var key = Utils.CardsToString(item2.Combination.ConvertToSmallCards(cardsNS));
            var probability = probabilities.GetValueOrDefault(key);
            foreach (var item in item2.Items)
            {
                var play = item.Play;
                var index = 0;
                while (index < play.Count() && index < main.Count() && play[index] == main[index])
                    index++;
                if (index >= play.Count() || index >= main.Count())
                    continue;
                var position = index % 4;
                if (position != 1 && position != 3)
                    continue; // seules les cartes des adversaires créent une variante
                var groupKey = (index, play[index]);
                if (!groups.TryGetValue(groupKey, out var group))
                    group = (0, -1, play, new HashSet<string>());
                if (group.Distributions.Add(key))
                    group.Probability += probability;
                if (probability > group.BestProbability || (probability == group.BestProbability && play.Count() > group.Play.Count()))
                {
                    group.BestProbability = probability;
                    group.Play = play;
                }
                groups[groupKey] = group;
            }
        }

        foreach (var ((index, card), group) in groups.OrderByDescending(g => g.Value.Probability))
        {
            var play = group.Play;
            var trickNumber = index / 4;
            var position = index % 4;
            var tricks = play.Chunk(4).ToList();
            var trick = tricks[trickNumber];
            var leadFromNorth = north.Contains(trick[0]);
            var who = position == 1 ? (leadFromNorth ? "Est" : "Ouest") : (leadFromNorth ? "Ouest" : "Est");
            var condition = card == Face.Dummy
                ? $"Si {who} ne fournit pas à la levée {trickNumber + 1}"
                : card == Face.SmallCard
                    ? $"Si {who} fournit petit à la levée {trickNumber + 1} au lieu {OfCard(main[index])}"
                    : $"Si {who} fournit {CardName(card)} à la levée {trickNumber + 1}";

            var played = new HashSet<Face>(play.Take(trickNumber * 4).Where(c => c != Face.SmallCard && c != Face.Dummy).Concat(result.Discarded ?? []));
            var steps = new List<string>();
            if (position == 1 && trick.Length >= 3 && trick[2] != Face.Dummy)
                steps.Add($"mettez {CardName(trick[2])} de {(leadFromNorth ? "Sud" : "Nord")}");
            foreach (var c in trick.Where(c => c != Face.SmallCard && c != Face.Dummy))
                played.Add(c);
            for (var t = trickNumber + 1; t < tricks.Count; t++)
            {
                var sentence = DescribeTrick(tricks[t], north, south, played);
                if (sentence == null)
                    break;
                steps.Add(char.ToLowerInvariant(sentence[0]) + sentence[1..].TrimEnd('.'));
                foreach (var c in tricks[t].Where(c => c != Face.SmallCard && c != Face.Dummy))
                    played.Add(c);
            }
            if (steps.Count == 0)
                steps.Add("rien ne change, continuez la ligne");

            var percent = (group.Probability * 100).ToString(group.Probability < 0.01 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
            var text = $"{condition} (≈ {percent} % des cas) : {string.Join(", puis ", steps)}.";
            if (!variants.Contains(text))
                variants.Add(text);
            if (variants.Count >= maxVariants)
                break;
        }
        return variants;
    }

    /// <summary>Une ligne du détail par répartition : mains adverses, probabilité, levées et déroulement.</summary>
    public record DistributionPlay(string West, string East, double Probability, int Tricks, string Play);

    /// <summary>
    /// Détail de la ligne pour chaque répartition adverse (regroupées comme le voit le déclarant),
    /// avec le déroulement joué par le juge pour la répartition la plus probable du groupe.
    /// </summary>
    public static List<DistributionPlay> DistributionPlays(SuitAnalysis analysis, LineItem line)
    {
        var result = analysis.Result;
        var cardsNS = result.North.Concat(result.South).OrderDescending().ToArray();
        var cardsEW = Utils.GetAllCards().Except(cardsNS).Except(result.Discarded ?? []).ToArray();
        return analysis.OutcomesOf(line)
            .Select(o =>
            {
                var east = Utils.StringToCardArray(o.Key);
                var west = cardsEW.Except(east).ToArray();
                return (o.Key, East: east, West: SmallHand(west, cardsNS), EastText: SmallHand(east, cardsNS), o.Value.Probability, o.Value.Tricks);
            })
            .GroupBy(r => (r.West, r.EastText, r.Tricks))
            .Select(g =>
            {
                var typical = g.MaxBy(r => r.Probability);
                var play = analysis.PlayOf(line, typical.Key);
                return new DistributionPlay(g.Key.West, g.Key.EastText, g.Sum(r => r.Probability), g.Key.Tricks,
                    DescribePlay(play, result.North, result.South, typical.East));
            })
            .OrderByDescending(r => r.Probability)
            .ToList();
    }

    private static string SmallHand(Face[] cards, Face[] cardsNS) =>
        cards.Length == 0 ? "—" : string.Join(" ", cards.ConvertToSmallCards(cardsNS).OrderDescending().Select(ShortName));

    /// <summary>
    /// Déroulement lisible, levée par levée, avec les vraies cartes :
    /// « 1. Sud A, Ouest 3, Nord 2, Est R (défense) ».
    /// </summary>
    public static string DescribePlay(IList<Face> play, Face[] north, Face[] south, Face[] east)
    {
        if (play == null || play.Count == 0)
            return "";
        var tricks = new List<string>();
        var number = 1;
        foreach (var trick in play.Chunk(4))
        {
            if (trick[0] == Face.Dummy)
                break;
            var seat = north.Contains(trick[0]) ? 0 : south.Contains(trick[0]) ? 2 : east.Contains(trick[0]) ? 1 : 3;
            var parts = new List<string>();
            var best = Face.Dummy;
            var bestSeat = seat;
            for (var i = 0; i < trick.Length; i++)
            {
                var who = (seat + i) % 4;
                parts.Add($"{SeatName(who)} {(trick[i] == Face.Dummy ? "défausse" : ShortName(trick[i]))}");
                if (trick[i] != Face.Dummy && trick[i] > best)
                {
                    best = trick[i];
                    bestSeat = who;
                }
            }
            var winner = trick.Length < 4 ? "" : bestSeat % 2 == 0 ? " (N-S)" : " (défense)";
            tricks.Add($"{number}. {string.Join(", ", parts)}{winner}");
            number++;
        }
        var remaining = north.Concat(south).Count(c => !play.Contains(c));
        var text = string.Join("  ·  ", tricks);
        var blocked = play.Chunk(4).Any(t => t[0] == Face.Dummy);
        if (remaining > 0)
            text += blocked
                ? "  ·  puis plus d'entrée dans la main qui a les cartes restantes"
                : "  ·  puis la défense n'a plus de carte : Nord-Sud jouent leurs cartes restantes";
        return text;
    }

    private static string SeatName(int seat) => seat switch { 0 => "Nord", 1 => "Est", 2 => "Sud", _ => "Ouest" };
}
