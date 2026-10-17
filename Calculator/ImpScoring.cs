namespace Calculator;

/// <summary>Couleur de l'atout du contrat.</summary>
public enum Strain { Clubs, Diamonds, Hearts, Spades, NoTrump }

/// <summary>
/// Contrat pour le mode IMP, comme dans SuitPlay original : le contrat, contré ou non, la vulnérabilité,
/// et le nombre de levées qu'il faut tirer de la couleur étudiée pour le réussir.
/// </summary>
public class ImpContract
{
    public int Level { get; init; } = 4;
    public Strain Strain { get; init; } = Strain.Spades;
    /// <summary>0 = non contré, 1 = contré, 2 = surcontré.</summary>
    public int Doubled { get; init; }
    public bool Vulnerable { get; init; }
    /// <summary>Levées à tirer de la couleur étudiée pour réussir exactement le contrat.</summary>
    public int TricksNeeded { get; init; }

    /// <summary>Marque du camp déclarant si la couleur rapporte <paramref name="suitTricks"/> levées.</summary>
    public int Score(int suitTricks)
    {
        var totalTricks = Math.Clamp(6 + Level - TricksNeeded + suitTricks, 0, 13);
        return ImpScoring.DuplicateScore(Level, Strain, Doubled, Vulnerable, totalTricks);
    }
}

/// <summary>Marque du bridge en duplicate et barème des IMP.</summary>
public static class ImpScoring
{
    /// <summary>Marque en duplicate (positive si le contrat est réussi, négative s'il chute).</summary>
    public static int DuplicateScore(int level, Strain strain, int doubled, bool vulnerable, int tricks)
    {
        var required = 6 + level;
        var multiplier = doubled switch { 1 => 2, 2 => 4, _ => 1 };

        if (tricks < required)
        {
            var down = required - tricks;
            if (doubled == 0)
                return -(vulnerable ? 100 : 50) * down;
            var penalty = 0;
            for (var i = 1; i <= down; i++)
            {
                if (vulnerable)
                    penalty += i == 1 ? 200 : 300;
                else
                    penalty += i == 1 ? 100 : i <= 3 ? 200 : 300;
            }
            return -penalty * (doubled == 2 ? 2 : 1);
        }

        int TrickValue(int trickNumber) => strain switch
        {
            Strain.Clubs or Strain.Diamonds => 20,
            Strain.Hearts or Strain.Spades => 30,
            _ => trickNumber == 1 ? 40 : 30
        };

        var contractPoints = 0;
        for (var i = 1; i <= level; i++)
            contractPoints += TrickValue(i) * multiplier;

        var score = contractPoints;
        score += contractPoints >= 100 ? (vulnerable ? 500 : 300) : 50;
        if (level == 6) score += vulnerable ? 750 : 500;
        if (level == 7) score += vulnerable ? 1500 : 1000;
        if (doubled == 1) score += 50;
        if (doubled == 2) score += 100;

        var overtricks = tricks - required;
        for (var i = 0; i < overtricks; i++)
        {
            score += doubled switch
            {
                1 => vulnerable ? 200 : 100,
                2 => vulnerable ? 400 : 200,
                _ => strain is Strain.Clubs or Strain.Diamonds ? 20 : 30
            };
        }
        return score;
    }

    private static readonly int[] ImpThresholds =
        [20, 50, 90, 130, 170, 220, 270, 320, 370, 430, 500, 600, 750, 900, 1100, 1300, 1500, 1750, 2000, 2250, 2500, 3000, 3500, 4000];

    /// <summary>Conversion d'un écart de points en IMP (avec le signe).</summary>
    public static int ToImps(int difference)
    {
        var abs = Math.Abs(difference);
        var imps = 0;
        while (imps < ImpThresholds.Length && abs >= ImpThresholds[imps])
            imps++;
        return Math.Sign(difference) * imps;
    }
}
