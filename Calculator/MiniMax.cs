using Calculator.Models;
using MoreLinq;

namespace Calculator;

public static class MiniMax
{
    public class MiniMaxSettings
    {
        public bool UseFalsecards { get; init; }

        /// <summary>
        /// Nombre d'entrées au mort (Nord) pour jouer la couleur depuis Nord ; null = illimité.
        /// Comme dans SuitPlay original : chaque entame depuis Nord coûte une entrée,
        /// sauf si Nord a remporté la levée précédente dans la couleur.
        /// </summary>
        public int? EntriesNorth { get; init; }

        /// <summary>Nombre d'entrées en Sud pour jouer la couleur depuis Sud ; null = illimité (même règle que pour Nord).</summary>
        public int? EntriesSouth { get; init; }
    }

    /// <summary>
    /// Levées restantes pour Nord-Sud quand la défense n'a plus de carte dans la couleur,
    /// en tenant compte des entrées au mort.
    /// </summary>
    public static int RemainingNSTricks(Dictionary<Player, List<Face>> initialCards, Cards playedCards, MiniMaxSettings settings)
    {
        var north = initialCards[Player.North].Except(playedCards.Data).ToList();
        var south = initialCards[Player.South].Except(playedCards.Data).ToList();
        var northMay = NorthMayLead(initialCards, playedCards, settings);
        var southMay = SouthMayLead(initialCards, playedCards, settings);
        if (northMay && southMay)
            return Math.Max(north.Count, south.Count);
        if (!northMay && !southMay)
            return 0;
        var (leader, partner) = northMay ? (north, south) : (south, north);
        if (leader.Count == 0)
            return 0;
        // Le partenaire peut prendre la main s'il a une carte plus haute que la plus petite du meneur, et la garder
        if (partner.Count > 0 && partner.Max() > leader.Min())
            return Math.Max(north.Count, south.Count);
        return leader.Count;
    }

    /// <summary>Nord peut-il entamer la prochaine levée, compte tenu des entrées disponibles ?</summary>
    public static bool NorthMayLead(Dictionary<Player, List<Face>> initialCards, Cards playedCards, MiniMaxSettings settings) =>
        MayLead(Player.North, settings?.EntriesNorth, initialCards, playedCards);

    /// <summary>Sud peut-il entamer la prochaine levée, compte tenu des entrées disponibles ?</summary>
    public static bool SouthMayLead(Dictionary<Player, List<Face>> initialCards, Cards playedCards, MiniMaxSettings settings) =>
        MayLead(Player.South, settings?.EntriesSouth, initialCards, playedCards);

    /// <summary>
    /// Chaque entame depuis la main coûte une entrée, sauf si cette main a remporté la levée précédente.
    /// </summary>
    private static bool MayLead(Player hand, int? entries, Dictionary<Player, List<Face>> initialCards, Cards playedCards)
    {
        if (entries == null)
            return true;
        var used = 0;
        var wonPrevious = false;
        foreach (var trick in playedCards.Chunk(4))
        {
            if (trick.Length == 0 || trick[0] == Face.Dummy)
                break;
            if (initialCards[hand].Contains(trick[0]) && !wonPrevious)
                used++;
            wonPrevious = trick.Length == 4 && initialCards[hand].Contains(trick.Max());
        }
        return wonPrevious || used < entries.Value;
    }

    public static List<Item> CalculateBestPlayForCombination(MiniMaxSettings miniMaxSettings, params IEnumerable<Face>[] cards)
    {
        var tree = new List<Item>();
        var initialCards = cards.Select((x, index) => (x, index)).ToDictionary(item => (Player)item.index, item => item.x.ToList());
        var cardsNS = initialCards[Player.North].Concat(initialCards[Player.South]).OrderDescending().ToList();
        var cardsEW = initialCards[Player.East].Concat(initialCards[Player.West]).OrderDescending().ToList();
        FindBestMove();
        return tree;

        void FindBestMove()
        {
            var playedCards = new Cards([]);
            var playableCards = GetPlayableCards(initialCards, playedCards, cardsEW, cardsNS, miniMaxSettings);
            foreach (var card in playableCards)
            {
                playedCards.Add(card);
                var resultItem = Minimax(playedCards, false);
                tree.Add(resultItem);
                playedCards.RemoveAt(playedCards.Count() - 1);
            }
        }

        Item Minimax(Cards playedCards, bool maximizingPlayer)
        {
            if (playedCards.Count(card => card != Face.Dummy) == initialCards.Values.Sum(x => x.Count) ||
                playedCards.Chunk(4).Last().First() == Face.Dummy)
            {
                var trickCount = GetTrickCount(playedCards, initialCards);
                return new Item(playedCards.Clone(), trickCount);
            }
            
            if (playedCards.Count() % 4 == 0 && !cardsEW.Except(playedCards.Data).Any()) 
            {
                var trickCount = GetTrickCount(playedCards, initialCards) +
                                 RemainingNSTricks(initialCards, playedCards, miniMaxSettings);
                return new Item(playedCards.Clone(), trickCount);
            }

            var playableCards = GetPlayableCards(initialCards, playedCards, cardsEW, cardsNS, miniMaxSettings);
            if (maximizingPlayer)
            {
                var resultItem = new Item (playedCards.Clone(), int.MinValue, []);
                foreach (var card in playableCards)
                {
                    playedCards.Add(card);
                    var value = Minimax(playedCards, false);
                    resultItem.Tricks = Math.Max(resultItem.Tricks, value.Tricks);
                    resultItem.Children.Add(value);
                    playedCards.RemoveAt(playedCards.Count() - 1);
                }
                if (playedCards.Any(x => x == Face.Dummy)) 
                    resultItem.Children = [resultItem.Children.First(x => x.Tricks == resultItem.Tricks)];
                    
                return resultItem;
            }
            else
            {
                var resultItem = new Item ( playedCards.Clone(),  int.MaxValue, []);
                foreach (var card in playableCards)
                {
                    playedCards.Add(card);
                    var value = Minimax(playedCards, true);
                    resultItem.Tricks = Math.Min(resultItem.Tricks, value.Tricks);

                    resultItem.Children.Add(value);
                    playedCards.RemoveAt(playedCards.Count() - 1);
                }

                if (playedCards.Any(x => x == Face.Dummy)) 
                    resultItem.Children = [resultItem.Children.First(x => x.Tricks == resultItem.Tricks)];
                resultItem.Children.RemoveAll(x => x.Tricks > resultItem.Tricks);

                return resultItem;
            }
        }

    }

    public static List<Face> GetPlayableCards(Dictionary<Player, List<Face>> initialCards, Cards playedCards,
        List<Face> cardsEW, List<Face> cardsNS, MiniMaxSettings miniMaxSettings)
    {
        var cardsEWNotPlayed = cardsEW.Except(playedCards.Data).ToList();
        if (playedCards.Count() % 4 == 0)
            return ApplyStrategyPosition1();

        var lastTrick = playedCards.Chunk(4).Last();
        var nextPlayer = NextPlayer(GetCurrentPlayer());
        var availableCards = initialCards[nextPlayer].Except(playedCards.Data).ToList();
            
        if (availableCards.Count == 0) 
            return [Face.Dummy];

        var playableCards = lastTrick.Length switch
        {
            1 => ApplyStrategyPosition2(),
            2 => ApplyStrategyPosition3(),
            3 => ApplyStrategyPosition4(),
            _ => availableCards
        };

        return playableCards.Count == 0 ? [Face.Dummy] : FilterAvailableCards(nextPlayer, playableCards) ;

        List<Face> ApplyStrategyPosition1()
        {
            var availableCardsNorth = initialCards[Player.North].Except(playedCards.Data).ToList();
            var availableCardsSouth = initialCards[Player.South].Except(playedCards.Data).ToList();
            var availableCardsNS = availableCardsNorth.Concat(availableCardsSouth).OrderDescending().ToList();
            if (availableCardsNS.Count == 0)
                return [Face.Dummy];
            // Entrées : si une main ne peut pas entamer, on joue depuis l'autre
            var northMay = NorthMayLead(initialCards, playedCards, miniMaxSettings);
            var southMay = SouthMayLead(initialCards, playedCards, miniMaxSettings);
            if (!northMay && !southMay)
                return [Face.Dummy];
            if (!northMay || !southMay)
            {
                var hand = northMay ? availableCardsNorth : availableCardsSouth;
                if (hand.Count == 0)
                    return [Face.Dummy];
                if (cardsEWNotPlayed.Count == 1)
                    return [hand.Max()];
                return AvailableCardsFiltered(hand, cardsEWNotPlayed).ToList();
            }
            // Play only high cards when the rest of the tricks is certain
            if (cardsEWNotPlayed.Count == 1)
                return [availableCardsNS.Max()];
            // Only play from the hand without forks
            var cardsResult = GetAvailableCardsForks(availableCardsNorth.ToArray(), availableCardsSouth.ToArray(), cardsEWNotPlayed.ToArray());
            return cardsResult;
        }
        
        List<Face> ApplyStrategyPosition2()
        {
            // TODO maybe use falsecards
            // Play lowest card when first hand plays a high one
            if (availableCards.All(x => x < lastTrick[0]))
                return [availableCards.Min()];
            // Play the lowest card when partner has no cards and a high card cannot benefit
            var player = NextPlayer(nextPlayer);
            var cardsNextPlayer = initialCards[player].Except(playedCards.Data).ToList();
            var cardsPartner = initialCards[NextPlayer(player)].Except(playedCards.Data).ToList();
            if (cardsPartner.Count == 0 && cardsNextPlayer.Count > 1 && availableCards.Count > 1 &&
                cardsNextPlayer.Max() > availableCards.Max() && cardsNextPlayer.Skip(1).Max() > availableCards.Skip(1).Max())
                return [availableCards.Min()];

            return availableCards;
        }
        
        List<Face> ApplyStrategyPosition3()
        {
            var playedCardsData = playedCards.Data.Chunk(4).SkipLast(1).SelectMany(x => x).ToList();
            var availableCardsNorth = initialCards[Player.North].Except(playedCardsData).ToList();
            var availableCardsSouth = initialCards[Player.South].Except(playedCardsData).ToList();
            
            // Play the lowest card when it's not possible to win the trick
            if (availableCards.All(x => x < lastTrick[0]) || availableCards.All(x => x < lastTrick[1]))
                return [availableCards.Min()];
            if (lastTrick[0] < lastTrick[1])
            {
                // Play a high card when 2nd hand plays the highest card
                if (lastTrick[1] > cardsEWNotPlayed.Max()) 
                    return availableCards.Where(x => x > lastTrick[1]).ToList();
                // Play a high card if you have the highest card
                if (availableCards.Any(x => x > cardsEWNotPlayed.Max()))
                    return availableCards.Where(x => x > lastTrick[1]).ToList();
                // Cover if it's free
                if (availableCards.Contains(lastTrick[1] + 1) &&
                    (availableCardsNorth.Contains(lastTrick[1] - 1) || availableCardsSouth.Contains(lastTrick[1] - 1)))
                    return availableCards.Where(x => x > lastTrick[1]).ToList();
            }
            
            // Don't play an unnecessary high card
            var partnersCards = GetCurrentPlayer() == Player.East ? availableCardsNorth : availableCardsSouth;
            if (lastTrick[1] != Face.Dummy && lastTrick[1] < lastTrick[0] && !partnersCards.All(x => x >= lastTrick[0])) 
                return [availableCards.Min()];

            // Don't play a card from the same segment as partner if you have cards from lower segments
            var cardsNSNotPlayed = availableCardsNorth.Concat(availableCardsSouth).OrderDescending();
            var segmentsNS = GetSegments(cardsNSNotPlayed, cardsEWNotPlayed).ToList();
            var lowerSegments = segmentsNS.SkipWhile(x => !x.Contains(lastTrick[0])).Skip(1).ToList();
            var higherSegments = segmentsNS.TakeWhile(x => !x.Contains(lastTrick[0])).ToList();
            if (availableCards.Any(x => lowerSegments.Any(y => y.Contains(x))))
                return higherSegments.SelectMany(x => x).Intersect(availableCards).Concat([availableCards.Min()]).ToList();

            return availableCards;
        }

        List<Face> ApplyStrategyPosition4()
        {
            // TODO maybe use falsecards
            var highestCardOtherTeam = (Face)Math.Max((int)lastTrick[0], (int)lastTrick[2]);
            var highestCardsOurTeam = availableCards.Where(x => x > highestCardOtherTeam && highestCardOtherTeam > lastTrick[1]).ToList();
            // Win is cheap is possible
            if (highestCardsOurTeam.Count > 0) 
                return [highestCardsOurTeam.Min()];
            return miniMaxSettings.UseFalsecards ? availableCards : [availableCards.Min()];
        }

        List<Face> FilterAvailableCards(Player player, List<Face> cards)
        {
            var cardsOtherTeam = player is Player.North or Player.South ? cardsEW : cardsNS;
            var playedCardsExceptLastTrick = playedCards.Data.Count > 4 ? playedCards.Data.Chunk(4).SkipLast(1).SelectMany(x => x): [];
            var cardsOtherTeamNotPlayed = cardsOtherTeam.Except(playedCardsExceptLastTrick).ToList();
            var availableCardsFiltered = AvailableCardsFiltered(cards, cardsOtherTeamNotPlayed, player is Player.West).ToList();

            return availableCardsFiltered.ToList();
        }

        Player GetCurrentPlayer()
        {
            var playerToLead = initialCards.Single(x => x.Value.Contains(lastTrick.First())).Key;
            return (Player)((lastTrick.Length + (int)playerToLead) % 4 - 1);
        }
    
    }
    
    static Player NextPlayer(Player player)
    {
        return player == Player.West ? Player.North : player + 1;
    }
    

    public static List<Face> GetAvailableCardsForks(Face[] cardsNorth, Face[] cardsSouth, Face[] cardsEW)
    {
        if (cardsNorth.Length == 1)
            return AvailableCardsFiltered(cardsNorth, cardsEW).ToList();
        if (cardsSouth.Length == 1)
            return AvailableCardsFiltered(cardsSouth, cardsEW).ToList();
        var longestSuit = Math.Min(Math.Max(cardsNorth.Length, cardsSouth.Length), cardsEW.Length);
        var cardsNS = cardsNorth.Concat(cardsSouth).OrderDescending().ToList();
        var segmentsNS = GetSegments(cardsNS, cardsEW).ToList();
        var lastRelevantSegment = segmentsNS.Single(x => x.Contains(cardsNS[longestSuit - 1])).ToList();
        var lastRelevantCard = lastRelevantSegment.Min();
        if (lastRelevantCard > cardsEW.Max())
            return AvailableCardsFiltered(lastRelevantSegment, cardsEW).ToList();
        // One or zero segment and the other more than one. Play the lowest of   
        var relevantSegmentsNorth = GetSegments(cardsNorth.Where(x => x >= lastRelevantCard), cardsEW).ToList();
        var relevantSegmentsSouth = GetSegments(cardsSouth.Where(x => x >= lastRelevantCard), cardsEW).ToList();
        if (cardsNorth.Length > 0 && relevantSegmentsNorth.Count < 2 && relevantSegmentsSouth.Count > 1 && LastSegmentIsTheSame(relevantSegmentsNorth, relevantSegmentsSouth))
            return cardsNorth.Length < cardsSouth.Length ? AvailableCardsFiltered(cardsNorth, cardsEW).ToList() : [cardsNorth.Min()];
        if (cardsSouth.Length > 0 && relevantSegmentsSouth.Count < 2 && relevantSegmentsNorth.Count > 1 && LastSegmentIsTheSame(relevantSegmentsNorth, relevantSegmentsSouth))
            return cardsSouth.Length < cardsNorth.Length ? AvailableCardsFiltered(cardsSouth, cardsEW).ToList() : [cardsSouth.Min()];
        // Both has one segment, play the lowest if the other player has the highest card
        if (relevantSegmentsNorth.Count == 1 && relevantSegmentsSouth.Count == 1)
        {
            var cardsPlayerNotHighestCard = cardsNorth.Contains(cardsNS.Max()) ? cardsSouth : cardsNorth;
            var segments = GetSegments(cardsPlayerNotHighestCard, cardsEW).ToList();
            if (segments.Count == 1)
                return [cardsPlayerNotHighestCard.Min()];
        }
        // Play a high card if we have the top highest cards and North and South lengths are the same
        if (relevantSegmentsNorth.Count > 1 && relevantSegmentsSouth.Count > 1 && cardsNS.Max() > cardsEW.Max())
        {
            if (segmentsNS[0].Count() > 1 && segmentsNS[1].Count() > 1 && cardsNorth.Length == cardsSouth.Length)
                return [cardsNorth.Max(), cardsSouth.Max()];
        }
        
        // Return north and south cards
        var cardsNorthFiltered = AvailableCardsFiltered(cardsNorth, cardsEW);
        var cardsSouthFiltered = AvailableCardsFiltered(cardsSouth, cardsEW);
        return cardsNorthFiltered.Concat(cardsSouthFiltered).ToList();
        
        bool LastSegmentIsTheSame(List<IEnumerable<Face>> relevantSegmentsNorth1, List<IEnumerable<Face>> relevantSegmentsSouth1)
        {
            if (relevantSegmentsNorth1.Count == 0 || relevantSegmentsSouth1.Count == 0)
                return true;
            var segmentNorth = GetSegmentNS(relevantSegmentsNorth1.Last(), segmentsNS);
            var segmentSouth = GetSegmentNS(relevantSegmentsSouth1.Last(), segmentsNS);
            return segmentNorth.SequenceEqual(segmentSouth);
            
            static IEnumerable<Face> GetSegmentNS(IEnumerable<Face> segment, IEnumerable<IEnumerable<Face>> segmentsNS)
            {
                return segmentsNS.Single(x => x.Contains(segment.First()));
            }
        }
    }
    
    private static IEnumerable<IEnumerable<Face>> GetSegments(IEnumerable<Face> cardsPlayer, IEnumerable<Face> cardsEW)
    {
        return cardsPlayer.Segment((item, prevItem, _) => cardsEW.Any(x => x > item && x < prevItem));
    }

    public static int GetTrickCount(Cards play, Dictionary<Player, List<Face>> initialCards)
    {
        return play.Chunk(4).Where(x => x.First() != Face.Dummy).Count(trick => 
            initialCards.Single(y => y.Value.Contains(trick.Max())).Key is Player.North or Player.South);
    }

    public static IEnumerable<Face> AvailableCardsFiltered(IEnumerable<Face> availableCards, IEnumerable<Face> availableCardsOtherTeam, bool takeFirst = false)
    {
        var segmentsAvailableCards = GetSegments(availableCards, availableCardsOtherTeam).ToList();
        var availableCardsFiltered = segmentsAvailableCards.Index(0).Select(x => takeFirst && x.Key == 0 ? x.Value.First() : x.Value.Last());
        return availableCardsFiltered;
    }
}