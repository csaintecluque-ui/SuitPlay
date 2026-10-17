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
    public static string DescribeLine(Cards line, Face[] north, Face[] south)
    {
        if (line == null || line.Count() == 0)
            return "Aucune ligne de jeu à afficher.";

        var played = new HashSet<Face>();
        var text = new StringBuilder();
        var number = 1;
        foreach (var trick in line.Chunk(4))
        {
            var sentence = DescribeTrick(trick, north, south, played);
            if (sentence == null)
                break;
            text.AppendLine($"{number}. {sentence}");
            number++;
            foreach (var card in trick.Where(c => c != Face.SmallCard && c != Face.Dummy))
                played.Add(card);
        }

        return text.ToString().TrimEnd();
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
    /// Variantes de la ligne : que faire quand un adversaire ne fournit pas la petite carte attendue
    /// (il fournit un honneur, ou n'a plus de carte dans la couleur).
    /// </summary>
    public static List<string> DescribeVariants(LineItem line, Face[] north, Face[] south, int maxVariants = 8)
    {
        var variants = new List<string>();
        if (line == null)
            return variants;
        var main = line.LongestLineIncludingGenerated;
        var others = line.Line.Concat(line.GeneratedLines)
            .Where(x => x != null && x != main)
            .Distinct()
            .OrderBy(x => x.Count())
            .ToList();

        foreach (var other in others)
        {
            // Première carte où la variante s'écarte de la ligne principale
            var index = 0;
            while (index < other.Count() && index < main.Count() && other[index] == main[index])
                index++;
            if (index >= other.Count())
                continue;

            var trickNumber = index / 4;
            var position = index % 4;
            var card = other[index];
            if (position != 1 && position != 3)
                continue; // seules les cartes des adversaires créent une variante

            var tricks = other.Chunk(4).ToList();
            var trick = tricks[trickNumber];
            var leadFromNorth = north.Contains(trick[0]);
            var who = position == 1 ? (leadFromNorth ? "Est" : "Ouest") : (leadFromNorth ? "Ouest" : "Est");
            var condition = card == Face.Dummy
                ? $"Si {who} ne fournit pas à la levée {trickNumber + 1}"
                : card == Face.SmallCard
                    ? $"Si {who} fournit petit à la levée {trickNumber + 1}"
                    : $"Si {who} fournit {CardName(card)} à la levée {trickNumber + 1}";

            var played = new HashSet<Face>(other.Take(trickNumber * 4).Where(c => c != Face.SmallCard && c != Face.Dummy));
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
                steps.Add("la suite ne change pas");

            var text = $"{condition} : {string.Join(", puis ", steps)}.";
            if (!variants.Contains(text))
                variants.Add(text);
            if (variants.Count >= maxVariants)
                break;
        }
        return variants;
    }
}
