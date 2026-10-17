using CommunityToolkit.Mvvm.ComponentModel;

namespace SuitPlay.ViewModels;

public partial class FilterViewModel : ObservableObject
{
    public FilterViewModel()
    {
        Load();
    }

    [ObservableProperty] public partial bool EnableVacantPlaces { get; set; }
    [ObservableProperty] public partial int VacantPlacesEast { get; set; } = 13;
    [ObservableProperty] public partial int VacantPlacesWest { get; set; } = 13;
    [ObservableProperty] public partial bool EnableEntriesNorth { get; set; }
    [ObservableProperty] public partial int EntriesNorth { get; set; } = 1;
    [ObservableProperty] public partial bool EnableEntriesSouth { get; set; }
    [ObservableProperty] public partial int EntriesSouth { get; set; } = 1;
    [ObservableProperty] public partial bool EnableImp { get; set; }
    [ObservableProperty] public partial int ImpLevel { get; set; } = 4;
    [ObservableProperty] public partial int ImpStrain { get; set; } = 3;
    [ObservableProperty] public partial int ImpDoubled { get; set; }
    [ObservableProperty] public partial bool ImpVulnerable { get; set; }
    [ObservableProperty] public partial int ImpTricksNeeded { get; set; } = 3;
    public List<string> Strains { get; } = ["♣ Trèfle", "♦ Carreau", "♥ Cœur", "♠ Pique", "Sans-atout"];
    public List<string> DoubledOptions { get; } = ["Non contré", "Contré", "Surcontré"];

    private void Load()
    {
        EnableVacantPlaces = Preferences.Get("EnableVacantPlaces", EnableVacantPlaces);
        VacantPlacesEast = Preferences.Get("VacantPlacesEast", VacantPlacesEast);
        VacantPlacesWest = Preferences.Get("VacantPlacesWest", VacantPlacesWest);
        EnableEntriesNorth = Preferences.Get(Constants.EnableEntriesNorth, false);
        EntriesNorth = Preferences.Get(Constants.EntriesNorth, 1);
        EnableEntriesSouth = Preferences.Get(Constants.EnableEntriesSouth, false);
        EntriesSouth = Preferences.Get(Constants.EntriesSouth, 1);
        EnableImp = Preferences.Get(Constants.EnableImp, false);
        ImpLevel = Preferences.Get(Constants.ImpLevel, 4);
        ImpStrain = Preferences.Get(Constants.ImpStrain, 3);
        ImpDoubled = Preferences.Get(Constants.ImpDoubled, 0);
        ImpVulnerable = Preferences.Get(Constants.ImpVulnerable, false);
        ImpTricksNeeded = Preferences.Get(Constants.ImpTricksNeeded, 3);
    }

    public void Save()
    {
        Preferences.Set("EnableVacantPlaces", EnableVacantPlaces);
        Preferences.Set("VacantPlacesEast", VacantPlacesEast);
        Preferences.Set("VacantPlacesWest", VacantPlacesWest);
        Preferences.Set(Constants.EnableEntriesNorth, EnableEntriesNorth);
        Preferences.Set(Constants.EntriesNorth, Math.Clamp(EntriesNorth, 0, 13));
        Preferences.Set(Constants.EnableEntriesSouth, EnableEntriesSouth);
        Preferences.Set(Constants.EntriesSouth, Math.Clamp(EntriesSouth, 0, 13));
        Preferences.Set(Constants.EnableImp, EnableImp);
        Preferences.Set(Constants.ImpLevel, Math.Clamp(ImpLevel, 1, 7));
        Preferences.Set(Constants.ImpStrain, Math.Clamp(ImpStrain, 0, 4));
        Preferences.Set(Constants.ImpDoubled, Math.Clamp(ImpDoubled, 0, 2));
        Preferences.Set(Constants.ImpVulnerable, ImpVulnerable);
        Preferences.Set(Constants.ImpTricksNeeded, Math.Clamp(ImpTricksNeeded, 0, 13));
    }
}