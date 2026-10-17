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

    /// <summary>Levées garanties par la ligne pour chaque répartition (clé = main d'Est réelle).</summary>
    /// <param name="singleDummy">
    /// true : une fois la ligne épuisée, le déclarant choisit sa carte sans voir les mains adverses
    /// (la même carte dans toutes les répartitions qu'il ne peut pas distinguer), comme dans l'original.
    /// false : il termine en voyant les cartes (plus rapide, mais trop optimiste).
    /// </param>
    public static Dictionary<string, (double Probability, int Tricks)> Outcomes(LineItem line, Result2 result, MiniMax.MiniMaxSettings settings = null, bool singleDummy = false)
    {
        settings ??= new MiniMax.MiniMaxSettings();
        if (!singleDummy)
            return EvaluateAll(line, result, settings, null, null);

        // Complète la ligne pas à pas : à chaque situation non prévue, on retient la carte
        // qui rapporte le plus de levées en moyenne sur les répartitions indiscernables.
        var extension = new Dictionary<string, Face>();
        Dictionary<string, (double Probability, int Tricks)> outcomes = null;
        for (var pass = 0; pass < 8; pass++)
        {
            var scores = new Dictionary<string, Dictionary<Face, double>>();
            outcomes = EvaluateAll(line, result, settings, extension, scores);
            if (scores.Count == 0)
                break;
            foreach (var (history, cardScores) in scores)
                extension[history] = cardScores.MaxBy(x => x.Value).Key;
        }
        return outcomes;
    }

    private static Dictionary<string, (double Probability, int Tricks)> EvaluateAll(LineItem line, Result2 result, MiniMax.MiniMaxSettings settings,
        Dictionary<string, Face> extension, Dictionary<string, Dictionary<Face, double>> scores)
    {
        var outcomes = new Dictionary<string, (double, int)>();
        var cardsNS = result.North.Concat(result.South).OrderDescending().ToArray();
        var cardsEW = Utils.GetAllCards().Except(cardsNS).OrderDescending().ToArray();
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
            var tricks = Evaluate(sequences, result.North, east, result.South, west, cardsNS, settings, extension, scores, share);
            outcomes[Utils.CardsToString(east)] = (share, tricks);
        }
        return outcomes;
    }

    /// <summary>Levées garanties par la ligne pour une répartition donnée.</summary>
    public static int Evaluate(List<Cards> sequences, Face[] north, Face[] east, Face[] south, Face[] west, Face[] cardsNS, MiniMax.MiniMaxSettings settings,
        Dictionary<string, Face> extension = null, Dictionary<string, Dictionary<Face, double>> scores = null, double probability = 0)
    {
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
        return Search(new Cards([]));

        int Search(Cards played)
        {
            var count = played.Count();
            if (played.Count(card => card != Face.Dummy) == totalCards ||
                (count > 0 && played.Chunk(4).Last().First() == Face.Dummy))
                return MiniMax.GetTrickCount(played, initialCards);
            if (count % 4 == 0 && !cardsEWList.Except(played.Data).Any())
                return MiniMax.GetTrickCount(played, initialCards) +
                       MiniMax.RemainingNSTricks(initialCards, played, settings);

            var memoKey = played.ToString();
            if (memo.TryGetValue(memoKey, out var known))
                return known;

            var position = count % 4;
            int value;
            if (position == 0 || position == 2)
            {
                // Au déclarant : il suit la ligne si elle prévoit ce cas
                var lineCard = NextCardFromLine(played, sequences, cardsNS);
                var legal = LegalCardsNS(played, initialCards, settings);
                if (lineCard.HasValue && legal.Contains(lineCard.Value))
                    value = Play(played, lineCard.Value);
                else
                {
                    var visibleKey = played.ConvertToSmallCards(cardsNS).ToString();
                    if (extension != null && extension.TryGetValue(visibleKey, out var chosen) && legal.Contains(chosen))
                    {
                        // Situation déjà tranchée sans voir les cartes adverses
                        value = Play(played, chosen);
                    }
                    else if (scores != null)
                    {
                        // On note ce que rapporte chaque carte possible, pour choisir ensuite sans tricher
                        var values = legal.Select(card => (Card: card, Value: Play(played, card))).ToList();
                        if (!scores.TryGetValue(visibleKey, out var cardScores))
                            scores[visibleKey] = cardScores = new Dictionary<Face, double>();
                        foreach (var (card, cardValue) in values)
                            cardScores[card] = cardScores.GetValueOrDefault(card) + probability * cardValue;
                        value = values.Max(x => x.Value);
                    }
                    else
                    {
                        // Hors de la ligne : il termine au mieux (en voyant les cartes)
                        var options = MiniMax.GetPlayableCards(initialCards, played, cardsEWList, cardsNSList, settings);
                        if (options.Count == 0) options = legal;
                        value = options.Max(card => Play(played, card));
                    }
                }
            }
            else
            {
                // À la défense : elle choisit la réponse la plus gênante parmi les coups du moteur,
                // plus la possibilité de couvrir (le moteur l'interdit parfois à tort en 2e main)
                var options = MiniMax.GetPlayableCards(initialCards, played, cardsEWList, cardsNSList, settings);
                if (position == 1)
                {
                    var cover = CheapestCover(played, initialCards);
                    if (cover.HasValue && !options.Contains(cover.Value))
                        options = options.Append(cover.Value).ToList();
                }
                value = options.Min(card => Play(played, card));
            }

            memo[memoKey] = value;
            return value;
        }

        int Play(Cards played, Face card)
        {
            played.Add(card);
            var value = Search(played);
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
            var all = (MiniMax.NorthMayLead(initialCards, played, settings) ? north.Concat(south) : south).ToList();
            return all.Count == 0 ? [Face.Dummy] : all;
        }
        // 3e main : le partenaire de celui qui a entamé
        var lead = played.Chunk(4).Last().First();
        var hand = initialCards[Player.North].Contains(lead) ? south : north;
        return hand.Count == 0 ? [Face.Dummy] : hand;
    }
}
