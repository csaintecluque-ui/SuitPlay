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

        var cardsNS = north.Concat(south).OrderDescending().ToList();
        var topSequence = GetTopSequence(cardsNS);
        var topIsMaster = topSequence.Contains(Face.Ace);

        var text = new StringBuilder();
        var number = 1;
        foreach (var trick in line.Chunk(4))
        {
            var sentence = DescribeTrick(trick, north, south, topSequence, topIsMaster);
            if (sentence == null)
                break;
            text.AppendLine($"{number}. {sentence}");
            number++;
        }

        return text.ToString().TrimEnd();
    }

    private static string DescribeTrick(Face[] trick, Face[] north, Face[] south, List<Face> topSequence, bool topIsMaster)
    {
        if (trick.Length == 0)
            return null;

        var lead = trick[0];
        if (!north.Contains(lead) && !south.Contains(lead))
            return "Les adversaires ont pris la main : reprenez la main et continuez la couleur.";

        var leadHand = north.Contains(lead) ? "Nord" : "Sud";
        var otherHand = leadHand == "Nord" ? "Sud" : "Nord";
        var secondHand = leadHand == "Nord" ? "Est" : "Ouest";

        if (trick.Length < 3)
            return $"Jouez {CardName(lead)} de {leadHand}.";

        var second = trick[1];
        var partner = trick[2];

        if (partner == Face.Dummy)
            return $"Jouez {CardName(lead)} de {leadHand} ({otherHand} n'a plus de carte dans la couleur).";

        var leadIsMaster = topIsMaster && topSequence.Contains(lead);
        var partnerIsMaster = topIsMaster && topSequence.Contains(partner);

        if (second != Face.SmallCard && second != Face.Dummy && !leadIsMaster)
            return $"Jouez {CardName(lead)} de {leadHand} vers {otherHand} ; si {secondHand} fournit {CardName(second)}, mettez {CardName(partner)}.";

        if (leadIsMaster)
            return $"Tirez {CardName(lead)} de {leadHand} (fournissez {CardName(partner)} de {otherHand}).";

        if (partnerIsMaster)
            return $"Jouez {CardName(lead)} de {leadHand} vers {otherHand} et prenez avec {CardName(partner)}.";

        if (lead < partner)
            return $"Jouez {CardName(lead)} de {leadHand} vers {otherHand} ; si {secondHand} fournit petit, mettez {CardName(partner)}.";

        return $"Avancez {CardName(lead)} de {leadHand} ; si {secondHand} fournit petit, laissez courir (fournissez {CardName(partner)} de {otherHand}).";
    }

    /// <summary>Plus haute séquence de cartes consécutives détenue par la ligne Nord-Sud.</summary>
    private static List<Face> GetTopSequence(List<Face> cardsNSDescending)
    {
        var result = new List<Face>();
        foreach (var card in cardsNSDescending)
        {
            if (result.Count > 0 && (int)result[^1] - (int)card != 1)
                break;
            result.Add(card);
        }
        return result;
    }
}
