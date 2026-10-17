namespace Calculator.Models;

public class Result2
{
    public List<DistributionItem> DistributionItems { get; init; }
    public List<LineItem> LineItems { get; init; }
    public List<int> PossibleNrOfTricks { get; init; }
    public Face[] North { get; init; }
    public Face[] South { get; init; }
    /// <summary>Cartes de Nord-Sud qui ne seront pas jouées dans la couleur (défausses ou coupes).</summary>
    public Face[] Discarded { get; init; } = [];
    public List<Face[]> CombinationsInSuitPlay { get; set; }
}