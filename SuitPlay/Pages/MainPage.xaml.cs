using System.Diagnostics;
using Calculator;
using Calculator.Models;
using SuitPlay.Services;
using SuitPlay.ViewModels;
using SuitPlay.Views;

namespace SuitPlay.Pages;

public partial class MainPage
{
    private HandView SelectedHandView
    {
        get;
        set
        {
            field = value;
            North.BackgroundColor = Colors.Transparent;
            South.BackgroundColor = Colors.Transparent;
            field.BackgroundColor = Color.FromArgb("#40512BD4");
        }
    }

    private readonly SettingsService settingsService;
    private readonly Dictionary<(string suit, string card), string> dictionary;
    private IDictionary<Face[], List<Item>> bestPlay;
    private Result2 result;

    public MainPage(SettingsService settingsService)
    {
        InitializeComponent();
        this.settingsService = settingsService;
        this.settingsService.SettingsChanged += (_, _) => EnableButtons(false);
        dictionary = SplitImages.Split(CardImageSettings.GetCardImageSettings("default"));
        Cards.OnImageTapped += TapGestureRecognizer_OnImageTapped;
        North.OnImageTapped += TapGestureRecognizer_OnImageTapped;
        North.OnHandTapped += TapGestureRecognizer_OnHandTapped;
        South.OnImageTapped += TapGestureRecognizer_OnImageTapped;
        South.OnHandTapped += TapGestureRecognizer_OnHandTapped;
        InitCards();
        LoadSettings();
        SelectedHandView = North;
    }

    private void LoadSettings()
    {
        var north = Preferences.Get("North", "AQ");
        var south = Preferences.Get("South", "32");
        var remainingCards = Utils.CardsToString(Utils.GetAllCards()).Except(north).Except(south);
        ((HandViewModel)North.BindingContext).ShowHand($"{north}", "default", dictionary);
        ((HandViewModel)South.BindingContext).ShowHand($"{south}", "default", dictionary);
        ((HandViewModel)Cards.BindingContext).ShowHand(new string(remainingCards.ToArray()), "default", dictionary);
    }
    
    private void SaveSettings()
    {
        Preferences.Set("North", Utils.CardsToString(GetHand(North)));
        Preferences.Set("South", Utils.CardsToString(GetHand(South)));
    }

    private void InitCards()
    {
        ((HandViewModel)Cards.BindingContext).ShowHand(Utils.CardsToString(Utils.GetAllCards()), "default", dictionary);
        ((HandViewModel)North.BindingContext).Cards.Clear();
        ((HandViewModel)South.BindingContext).Cards.Clear();
    }

    private void TapGestureRecognizer_OnImageTapped(object sender, HandView handView)
    {
        var card = (UiCard)((Image)sender).BindingContext;
        ((HandViewModel)handView?.BindingContext)?.RemoveCard(card);
        ((HandViewModel)(handView == Cards ? SelectedHandView : Cards).BindingContext).AddCard(card);
        SaveSettings();
        EnableButtons(false);
    }

    private void EnableButtons(bool enable)
    {
        DistributionsButton.IsEnabled = enable;
    }

    private void TapGestureRecognizer_OnHandTapped(object sender, HandView handView)
    {
        SelectedHandView = handView;
    }
    
    private void ResetButton_OnClicked(object sender, EventArgs e)
    {
        InitCards();
        SaveSettings();
        EnableButtons(false);
        SelectedHandView = North;
    }

    private async void CalculateButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            CalculateButton.IsEnabled = false;
            ResultsPanel.IsVisible = false;
            StatusLabel.Text = "Calcul en cours…";
            var stopWatch = Stopwatch.StartNew();
            var northHand = GetHand(North);
            var southHand = GetHand(South);
            if (northHand.Length + southHand.Length == 0)
            {
                StatusLabel.Text = "Donnez d'abord des cartes à Nord et à Sud.";
                return;
            }
            var miniMaxSettings = new MiniMax.MiniMaxSettings {UseFalsecards = Preferences.Get(Constants.UseFalsecards, false)} ;
            bestPlay = await Task.Run(() => Calculate.CalculateBestPlay(northHand, southHand, miniMaxSettings));
            var calculateSettings = new Calculate.CalculateSettings
                { EtalonsDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "Calculator.etalons_suitplay"), 
                    MaxLines = Preferences.Get(Constants.MaxLinesInCalculate, 10000),
                    FilterInferiorLines = Preferences.Get(Constants.FilterInferiorLines, false),
                    RemoveDuplicateLines = Preferences.Get(Constants.RemoveDuplicateLines, false),
                    RemoveTranspositions = Preferences.Get(Constants.RemoveTranspositions, false)
                };
            if (Preferences.Get(Constants.EnableVacantPlaces, true))
            {
                calculateSettings.VacantPlacesWest = Preferences.Get(Constants.VacantPlacesWest, 13);
                calculateSettings.VacantPlacesEast = Preferences.Get(Constants.VacantPlacesEast, 13);
            }
            
            result = await Task.Run(() => Calculate.GetResult2(bestPlay, northHand, southHand, calculateSettings));
            StatusLabel.Text = $"Nord : {FrenchText.HandToString(northHand)}    Sud : {FrenchText.HandToString(southHand)}    (calcul en {stopWatch.Elapsed.TotalSeconds:0.0} s)";
            ShowResults();
            EnableButtons(true);
        }
        catch (MaxLinesException)
        {
            StatusLabel.Text = "Calcul interrompu : trop de lignes de jeu.";
            await DisplayAlertAsync("Trop de lignes de jeu", "Le calcul a généré trop de lignes de jeu.\nSimplifiez la combinaison ou augmentez « Lignes max. dans le calcul » dans les réglages.", "OK");
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "Le calcul a échoué.";
            await DisplayAlertAsync("Erreur", exception.Message, "OK");
        }
        finally
        {
            CalculateButton.IsEnabled = true;
        }
    }

    // Objectifs proposés : null = maximum de levées en moyenne, sinon « au moins N levées ».
    private readonly List<int?> goals = [];

    private void ShowResults()
    {
        if (result?.LineItems == null || result.LineItems.Count == 0)
        {
            StatusLabel.Text += "\nAucune ligne de jeu trouvée.";
            return;
        }

        var allTricks = bestPlay.SelectMany(x => x.Value).Select(x => x.Tricks).ToList();
        guaranteedTricks = allTricks.Min();
        var maxTricks = allTricks.Max();
        var tricks = result.PossibleNrOfTricks.Where(x => x > guaranteedTricks).ToList();
        if (guaranteedTricks > 0)
            tricks.Add(guaranteedTricks);
        tricks = tricks.Distinct().OrderByDescending(x => x).ToList();

        // Tableau des chances : une ligne par objectif
        ResultsGrid.Children.Clear();
        ResultsGrid.RowDefinitions.Clear();
        AddResultRow(0, "Objectif", "Chances", "Première carte", true);
        var row = 1;
        AddResultRow(row++, $"{maxTricks + 1} levées ou plus", "impossible", "");
        foreach (var goal in tricks)
        {
            var line = BestLineFor(goal);
            var probability = ProbabilityFor(line, goal);
            var chances = goal <= guaranteedTricks ? "100 % (assuré)" : $"{probability * 100:0.0} %";
            AddResultRow(row++, $"Au moins {goal} levée{(goal > 1 ? "s" : "")}", chances, FirstCardText(line));
        }
        var bestAverage = FrenchText.BestLineFor(result.LineItems, null);
        AddResultRow(row, "Maximum de levées", $"{bestAverage.Average.Value:0.00} en moyenne", FirstCardText(bestAverage));

        // Sélecteur d'objectif
        goals.Clear();
        var items = new List<string>();
        foreach (var goal in tricks)
        {
            goals.Add(goal);
            items.Add($"Faire au moins {goal} levée{(goal > 1 ? "s" : "")}");
        }
        goals.Add(null);
        items.Add("Faire le maximum de levées (moyenne)");
        GoalPicker.ItemsSource = items;
        GoalPicker.SelectedIndex = 0;

        ResultsPanel.IsVisible = true;
        ShowSelectedGoal();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), async () =>
        {
            try { await MainScroll.ScrollToAsync(ResultsPanel, ScrollToPosition.Start, true); }
            catch { /* défilement facultatif */ }
        });
    }

    private int guaranteedTricks;

    private LineItem BestLineFor(int? goal)
    {
        return goal == null || goal <= guaranteedTricks
            ? FrenchText.BestLineFor(result.LineItems, null)
            : FrenchText.BestLineFor(result.LineItems, goal);
    }

    private double ProbabilityFor(LineItem line, int goal)
    {
        return goal <= guaranteedTricks ? 1.0 : FrenchText.ProbabilityFor(line, goal);
    }

    private void AddResultRow(int row, string goal, string chances, string firstCard, bool header = false)
    {
        ResultsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var attributes = header ? FontAttributes.Bold : FontAttributes.None;
        var labels = new[] { goal, chances, firstCard };
        for (var column = 0; column < labels.Length; column++)
        {
            var label = new Label { Text = labels[column], FontAttributes = attributes };
            ResultsGrid.Add(label, column, row);
        }
    }

    private string FirstCardText(LineItem line)
    {
        var sequence = line?.LongestLineIncludingGenerated;
        if (sequence == null || sequence.Count() == 0)
            return "";
        var first = sequence.First();
        var hand = GetHand(North).Contains(first) ? "Nord" : "Sud";
        return $"{FrenchText.ShortName(first)} de {hand}";
    }

    private void GoalPicker_OnSelectedIndexChanged(object sender, EventArgs e)
    {
        ShowSelectedGoal();
    }

    private void ShowSelectedGoal()
    {
        if (result == null || GoalPicker.SelectedIndex < 0 || GoalPicker.SelectedIndex >= goals.Count)
            return;

        var goal = goals[GoalPicker.SelectedIndex];
        var line = BestLineFor(goal);
        if (line == null)
            return;

        GoalSummary.Text = goal == null
            ? $"Cette ligne rapporte en moyenne {line.Average.Value:0.00} levées."
            : goal.Value <= guaranteedTricks
                ? $"{goal} levée{(goal > 1 ? "s" : "")} assurée{(goal > 1 ? "s" : "")} quelle que soit la répartition."
                : $"Cette ligne réussit dans {ProbabilityFor(line, goal.Value) * 100:0.0} % des cas.";
        var sequence = line.LongestLineIncludingGenerated;
        LineDescription.Text = FrenchText.DescribeLine(sequence, result.North, result.South);
        var variants = FrenchText.DescribeVariants(line, result);
        VariantsTitle.IsVisible = variants.Count > 0;
        VariantsText.IsVisible = variants.Count > 0;
        VariantsText.Text = string.Join("\n", variants.Select(v => "• " + v));
        LineSequence.Text = $"Séquence (Nord-Sud et adversaires, x = petite carte) : {FrenchText.SequenceToString(sequence)}";
    }

    private static Face[] GetHand(HandView handView)
    {
        return ((HandViewModel)handView.BindingContext).Cards.Select(y => y.Face).ToArray();
    }

    private async void DistributionsButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(DistributionsPage2), new Dictionary<string, object> { ["Result"] = result});
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Erreur", $"{exception.Message}\n{exception.StackTrace}\n", "OK");
        }
    }
    
    private async void SettingsButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(SettingsPage));
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Erreur", exception.Message, "OK");
        }
    }

    private async void OtherFactorsButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(FilterPage));
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Erreur", exception.Message, "OK");
        }
    }
}