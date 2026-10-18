using Calculator.Models;

namespace Calculator;

/// <summary>
/// « Juge » d'une ligne de jeu, à la manière de SuitPlay original : pour chaque répartition adverse,
/// le déclarant suit la ligne (il ne voit que ses cartes et celles déjà jouées) et la défense,
/// qui voit tout, choisit à chaque fois la réponse la plus gênante. Le résultat est le nombre de levées
/// que la ligne garantit contre toute défense.
/// Une fois la ligne épuisée, le déclarant termine au mieux.
/// </summary>
public static class LineEvaluator
{
    /// <summary>Diagnostic : choix retenus hors de la ligne lors du dernier calcul « sans voir les cartes ».</summary>
    public static Dictionary<string, Face> LastExtension { get; private set; }

    /// <summary>Levées garanties par la ligne pour chaque répartition (clé = main d'Est réelle).</summary>
    /// <param name="singleDummy">
    /// true : une fois la ligne épuisée, le déclarant choisit sa carte sans voir les mains adverses
    /// (la même carte dans toutes les répartitions qu'il ne peut pas distinguer), comme dans l'original.
    /// false : il termine en voyant les cartes (plus rapide, mais trop optimiste).
    /// </param>
    /// <param name="goal">
    /// Avec <paramref name="singleDummy"/> : objectif qui guide les choix hors de la ligne
    /// (null = maximum de levées en moyenne, sinon chances d'atteindre ce nombre de levées).
    /// </param>
    public static Dictionary<string, (double Probability, int Tricks)> Outcomes(LineItem line, Result2 result, MiniMax.MiniMaxSettings settings = null,
        bool singleDummy = false, int? goal = null, bool guessesOnly = false, JudgeCache cache = null)
    {
        settings ??= new MiniMax.MiniMaxSettings();
        if (!singleDummy)
            return EvaluateAll(line, result, settings, null, null, null, null, null, cache);

        Func<int, double> utility = goal == null
            ? tricks => tricks
            : tricks => (tricks >= goal.Value ? 1.0 : 0.0) + 0.001 * tricks;
        Func<int, int> primary = goal == null ? tricks => tricks : tricks => tricks >= goal.Value ? 1 : 0;

        // Complète la ligne par itérations successives : à chaque situation non prévue par la ligne,
        // le déclarant retient la carte la meilleure pour l'objectif, en moyenne sur les répartitions
        // qu'il ne peut pas distinguer ; les choix sont révisés jusqu'à ce qu'ils ne bougent plus.
        var extension = new Dictionary<string, Face>();
        Dictionary<string, (double Probability, int Tricks)> outcomes = null;
        for (var pass = 0; pass < 12; pass++)
        {
            var scores = new Dictionary<string, Dictionary<Face, double>>();
            var suboptimal = guessesOnly ? new Dictionary<string, HashSet<Face>>() : null;
            outcomes = EvaluateAll(line, result, settings, extension, scores, utility, suboptimal, primary);
            var changed = false;
            foreach (var (history, cardScores) in scores)
            {
                // Variante « seulement les vrais choix » : si une même carte est la meilleure dans toutes les répartitions
                // possibles, le déclarant n'a rien à deviner et termine au mieux
                if (suboptimal != null && cardScores.Keys.Any(card => !suboptimal[history].Contains(card)))
                {
                    if (extension.Remove(history))
                        changed = true;
                    continue;
                }
                var best = cardScores.MaxBy(x => x.Value).Key;
                if (!extension.TryGetValue(history, out var current) || current != best &&
                    cardScores[best] > cardScores.GetValueOrDefault(current, double.MinValue) + 1e-12)
                {
                    extension[history] = best;
                    changed = true;
                }
            }
            if (!changed)
                break;
        }
        LastExtension = extension;
        return outcomes;
    }

    private static Dictionary<string, (double Probability, int Tricks)> EvaluateAll(LineItem line, Result2 result, MiniMax.MiniMaxSettings settings,
        Dictionary<string, Face> extension, Dictionary<string, Dictionary<Face, double>> scores, Func<int, double> utility,
        Dictionary<string, HashSet<Face>> suboptimal, Func<int, int> primary, JudgeCache cache = null)
    {
        var outcomes = new Dictionary<string, (double, int)>();
        var cardsNS = result.North.Concat(result.South).OrderDescending().ToArray();
        var cardsEW = Utils.GetAllCards().Except(cardsNS).Except(result.Discarded ?? []).OrderDescending().ToArray();
        var sequences = line.Line.Concat(line.GeneratedLines).Where(x => x != null && x.Count() > 0).Distinct().ToList();

        // Probabilité par main d'Est (en petites cartes), telle que calculée par le moteur
        var probabilityByEast = new Dictionary<string, double>();
        foreach (var distribution in result.DistributionItems)
        {
            var key = Utils.CardsToString(distribution.East);
            probabilityByEast[key] = probabilityByEast.GetValueOrDefault(key) + distribution.Probability;
        }
        var classesByEast = line.Items2
            .GroupBy(i => Utils.CardsToString(i.Combination.ConvertToSmallCards(cardsNS)))
            .ToDictionary(g => g.Key, g => g.Count());

        // Les répartitions du moteur et celles de la ligne sont dans le même ordre : on les apparie par position
        var paired = line.Items2.Count == result.DistributionItems.Count;
        for (var index = 0; index < line.Items2.Count; index++)
        {
            var item = line.Items2[index];
            var east = item.Combination;
            var west = cardsEW.Except(east).OrderDescending().ToArray();
            var eastKey = Utils.CardsToString(east.ConvertToSmallCards(cardsNS));
            double share;
            if (paired && Utils.CardsToString(result.DistributionItems[index].East) == eastKey)
                share = result.DistributionItems[index].Probability;
            else if (probabilityByEast.TryGetValue(eastKey, out var probability))
                share = probability / classesByEast[eastKey];
            else
                continue;
            var tricks = Evaluate(sequences, result.North, east, result.South, west, cardsNS, settings, extension, scores, share, utility,
                suboptimal: suboptimal, primary: primary, tailCache: cache?.For(east));
            outcomes[Utils.CardsToString(east)] = (share, tricks);
        }
        return outcomes;
    }

    /// <summary>
    /// Déroulement de la ligne contre une main d'Est donnée (cartes réelles, quatre par levée,
    /// <see cref="Face.Dummy"/> quand un joueur n'a plus de carte), tel que le juge le joue.
    /// </summary>
    public static List<Face> PlayFor(LineItem line, Result2 result, Face[] east, MiniMax.MiniMaxSettings settings = null, JudgeCache cache = null)
    {
        settings ??= new MiniMax.MiniMaxSettings();
        var cardsNS = result.North.Concat(result.South).OrderDescending().ToArray();
        var cardsEW = Utils.GetAllCards().Except(cardsNS).Except(result.Discarded ?? []).OrderDescending().ToArray();
        var sequences = line.Line.Concat(line.GeneratedLines).Where(x => x != null && x.Count() > 0).Distinct().ToList();
        var west = cardsEW.Except(east).OrderDescending().ToArray();
        var play = new List<Face>();
        Evaluate(sequences, result.North, east, result.South, west, cardsNS, settings, principalPlay: play, tailCache: cache?.For(east));
        return play;
    }

    /// <summary>Levées garanties par la ligne pour une répartition donnée.</summary>
    public static int Evaluate(List<Cards> sequences, Face[] north, Face[] east, Face[] south, Face[] west, Face[] cardsNS, MiniMax.MiniMaxSettings settings,
        Dictionary<string, Face> extension = null, Dictionary<string, Dictionary<Face, double>> scores = null, double probability = 0,
        Func<int, double> utility = null, List<Face> principalPlay = null,
        Dictionary<string, HashSet<Face>> suboptimal = null, Func<int, int> primary = null, Dictionary<string, int> tailCache = null)
    {
        utility ??= tricks => tricks;
        // Une fois la ligne épuisée (plus aucune séquence ne prolonge la situation), la suite ne dépend plus de la ligne :
        // sa valeur est partagée entre toutes les lignes jugées pour cette répartition
        var shareTails = tailCache != null && extension == null && scores == null;
        var initialCards = new Dictionary<Player, List<Face>>
        {
            [Player.North] = north.ToList(),
            [Player.East] = east.ToList(),
            [Player.South] = south.ToList(),
            [Player.West] = west.ToList(),
        };
        var cardsNSList = cardsNS.ToList();
        var cardsEWList = east.Concat(west).OrderDescending().ToList();
        var totalCards = initialCards.Values.Sum(x => x.Count);
        var memo = new Dictionary<string, int>();
        var total = Search(new Cards([]));
        if (principalPlay != null && extension == null && scores == null)
            principalPlay.AddRange(Principal());
        return total;

        // Déroulement retenu par le juge : le déclarant suit la ligne, la défense joue sa meilleure réponse
        // (à valeur égale, la plus petite carte) ; le calcul est déjà en mémoire, la reconstitution est immédiate.
        List<Face> Principal()
        {
            var played = new Cards([]);
            while (true)
            {
                var count = played.Count();
                if (played.Count(card => card != Face.Dummy) == totalCards ||
                    (count > 0 && played.Chunk(4).Last().First() == Face.Dummy) ||
                    (count % 4 == 0 && !cardsEWList.Except(played.Data).Any()))
                    break;
                var position = count % 4;
                Face chosen;
                if (position == 0 || position == 2)
                {
                    var lineCard = NextCardFromLine(played, sequences, cardsNS);
                    var legal = LegalCardsNS(played, initialCards, settings);
                    if (lineCard.HasValue && legal.Contains(lineCard.Value))
                        chosen = lineCard.Value;
                    else
                    {
                        var options = MiniMax.GetPlayableCards(initialCards, played, cardsEWList, cardsNSList, settings);
                        if (options.Count == 0) options = legal;
                        chosen = options.Select(card => (Card: card, Value: Play(played, card)))
                            .OrderByDescending(x => x.Value).ThenBy(x => x.Card).First().Card;
                    }
                }
                else
                {
                    var options = DefenceOptions(played, initialCards, cardsEWList, cardsNSList, cardsNS, settings);
                    chosen = options.Select(card => (Card: card, Value: Play(played, card)))
                        .OrderBy(x => x.Value).ThenBy(x => x.Card).First().Card;
                }
                played.Add(chosen);
            }
            return played.Data.ToList();
        }

        int Search(Cards played, bool offline = false)
        {
            var count = played.Count();
            if (played.Count(card => card != Face.Dummy) == totalCards ||
                (count > 0 && played.Chunk(4).Last().First() == Face.Dummy))
                return MiniMax.GetTrickCount(played, initialCards);
            if (count % 4 == 0 && !cardsEWList.Except(played.Data).Any())
                return MiniMax.GetTrickCount(played, initialCards) +
                       MiniMax.RemainingNSTricks(initialCards, played, settings);

            var memoKey = played.ToString();
            var cache = offline && shareTails ? tailCache : memo;
            if (cache.TryGetValue(memoKey, out var known))
                return known;

            var position = count % 4;
            int value;
            if (position == 0 || position == 2)
            {
                // Au déclarant : il suit la ligne si elle prévoit ce cas
                var lineCard = offline ? null : NextCardFromLine(played, sequences, cardsNS);
                if (offline && shareTails)
                {
                    var options = MiniMax.GetPlayableCards(initialCards, played, cardsEWList, cardsNSList, settings);
                    if (options.Count == 0) options = LegalCardsNS(played, initialCards, settings);
                    value = options.Max(card => Play(played, card, true));
                    tailCache[memoKey] = value;
                    return value;
                }
                var legal = LegalCardsNS(played, initialCards, settings);
                if (lineCard.HasValue && legal.Contains(lineCard.Value))
                    value = Play(played, lineCard.Value);
                else
                {
                    var visibleKey = played.ConvertToSmallCards(cardsNS).ToString();
                    var hasChoice = extension != null && extension.TryGetValue(visibleKey, out var chosen) && legal.Contains(chosen);
                    if (scores != null)
                    {
                        // On note ce que rapporte chaque carte possible (pour réviser le choix sans tricher),
                        // mais on joue la carte déjà retenue si elle existe
                        var values = legal.Select(card => (Card: card, Value: Play(played, card))).ToList();
                        if (!scores.TryGetValue(visibleKey, out var cardScores))
                            scores[visibleKey] = cardScores = new Dictionary<Face, double>();
                        foreach (var (card, cardValue) in values)
                            cardScores[card] = cardScores.GetValueOrDefault(card) + probability * utility(cardValue);
                        if (suboptimal != null)
                        {
                            if (!suboptimal.TryGetValue(visibleKey, out var worse))
                                suboptimal[visibleKey] = worse = [];
                            var bestPrimary = values.Max(x => primary(x.Value));
                            foreach (var (card, cardValue) in values)
                                if (primary(cardValue) < bestPrimary)
                                    worse.Add(card);
                        }
                        value = hasChoice ? values.First(x => x.Card == extension[visibleKey]).Value : values.Max(x => x.Value);
                    }
                    else if (hasChoice)
                    {
                        // Situation déjà tranchée sans voir les cartes adverses
                        value = Play(played, extension[visibleKey]);
                    }
                    else
                    {
                        // Hors de la ligne : il termine au mieux (en voyant les cartes)
                        var options = MiniMax.GetPlayableCards(initialCards, played, cardsEWList, cardsNSList, settings);
                        if (options.Count == 0) options = legal;
                        var lineOver = !lineCard.HasValue;
                        value = options.Max(card => Play(played, card, lineOver));
                    }
                }
            }
            else
            {
                // À la défense : elle choisit la réponse la plus gênante parmi les coups du moteur,
                // plus la possibilité de couvrir (le moteur l'interdit parfois à tort en 2e main)
                var options = DefenceOptions(played, initialCards, cardsEWList, cardsNSList, cardsNS, settings);
                value = options.Min(card => Play(played, card, offline));
            }

            cache[memoKey] = value;
            return value;
        }

        int Play(Cards played, Face card, bool offline = false)
        {
            played.Add(card);
            var value = Search(played, offline);
            played.RemoveAt(played.Count() - 1);
            return value;
        }
    }

    /// <summary>Carte prévue par la ligne pour la situation vue par le déclarant, ou null si la ligne ne la prévoit pas.</summary>
    private static Face? NextCardFromLine(Cards played, List<Cards> sequences, Face[] cardsNS)
    {
        var visible = played.ConvertToSmallCards(cardsNS);
        var n = visible.Count();
        foreach (var sequence in sequences)
        {
            if (sequence.Count() <= n)
                continue;
            var match = true;
            for (var i = 0; i < n && match; i++)
                match = sequence[i] == visible[i];
            if (match)
                return sequence[n];
        }
        return null;
    }

    /// <summary>
    /// Cartes que la défense peut envisager. Par défaut : les coups du moteur, plus la possibilité de couvrir
    /// en 2e main. Avec <see cref="MiniMax.MiniMaxSettings.FullDefence"/> : toutes ses cartes
    /// (une seule par « petite carte », elles sont équivalentes pour le déclarant).
    /// </summary>
    private static List<Face> DefenceOptions(Cards played, Dictionary<Player, List<Face>> initialCards,
        List<Face> cardsEWList, List<Face> cardsNSList, Face[] cardsNS, MiniMax.MiniMaxSettings settings)
    {
        var position = played.Count() % 4;
        if (settings?.FullDefence == true)
        {
            var trick = played.Chunk(4).Last();
            var leader = initialCards.First(x => x.Value.Contains(trick[0])).Key;
            var player = (Player)(((int)leader + trick.Length) % 4);
            var cards = initialCards[player].Except(played.Data).ToList();
            if (cards.Count == 0)
                return [Face.Dummy];
            // Les « petites cartes » (notées x) sont interchangeables : on n'en garde qu'une
            var converted = cards.ConvertToSmallCards(cardsNS);
            var small = cards.Where((c, i) => converted[i] == Face.SmallCard).ToList();
            var result = cards.Where((c, i) => converted[i] != Face.SmallCard).ToList();
            if (small.Count > 0)
                result.Add(small.Min());
            return result;
        }
        var options = MiniMax.GetPlayableCards(initialCards, played, cardsEWList, cardsNSList, settings);
        if (position == 1)
        {
            var cover = CheapestCover(played, initialCards);
            if (cover.HasValue && !options.Contains(cover.Value))
                options = options.Append(cover.Value).ToList();
        }
        return options;
    }

    /// <summary>Plus petite carte du 2e joueur qui bat la carte d'entame, s'il en a une.</summary>
    private static Face? CheapestCover(Cards played, Dictionary<Player, List<Face>> initialCards)
    {
        var trick = played.Chunk(4).Last();
        var leader = initialCards.First(x => x.Value.Contains(trick[0])).Key;
        var player = (Player)(((int)leader + trick.Length) % 4);
        var higher = initialCards[player].Except(played.Data).Where(c => c > trick[0]).ToList();
        return higher.Count == 0 ? null : higher.Min();
    }

    /// <summary>Cartes que Nord-Sud peuvent légalement jouer maintenant.</summary>
    private static List<Face> LegalCardsNS(Cards played, Dictionary<Player, List<Face>> initialCards, MiniMax.MiniMaxSettings settings)
    {
        var position = played.Count() % 4;
        var north = initialCards[Player.North].Except(played.Data).ToList();
        var south = initialCards[Player.South].Except(played.Data).ToList();
        if (position == 0)
        {
            var all = (MiniMax.NorthMayLead(initialCards, played, settings) ? north : [])
                .Concat(MiniMax.SouthMayLead(initialCards, played, settings) ? south : []).ToList();
            return all.Count == 0 ? [Face.Dummy] : all;
        }
        // 3e main : le partenaire de celui qui a entamé
        var lead = played.Chunk(4).Last().First();
        var hand = initialCards[Player.North].Contains(lead) ? south : north;
        return hand.Count == 0 ? [Face.Dummy] : hand;
    }

    /// <summary>
    /// Mémoire partagée par toutes les lignes jugées pour une même combinaison : valeur des fins de coup
    /// (une fois la ligne épuisée), par main d'Est.
    /// </summary>
    public class JudgeCache
    {
        private readonly Dictionary<string, Dictionary<string, int>> tails = new();

        public Dictionary<string, int> For(Face[] east)
        {
            var key = Utils.CardsToString(east);
            if (!tails.TryGetValue(key, out var tail))
                tails[key] = tail = new Dictionary<string, int>();
            return tail;
        }
    }
}
