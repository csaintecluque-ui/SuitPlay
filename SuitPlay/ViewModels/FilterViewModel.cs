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

    private void Load()
    {
        EnableVacantPlaces = Preferences.Get("EnableVacantPlaces", EnableVacantPlaces);
        VacantPlacesEast = Preferences.Get("VacantPlacesEast", VacantPlacesEast);
        VacantPlacesWest = Preferences.Get("VacantPlacesWest", VacantPlacesWest);
        EnableEntriesNorth = Preferences.Get(Constants.EnableEntriesNorth, false);
        EntriesNorth = Preferences.Get(Constants.EntriesNorth, 1);
    }

    public void Save()
    {
        Preferences.Set("EnableVacantPlaces", EnableVacantPlaces);
        Preferences.Set("VacantPlacesEast", VacantPlacesEast);
        Preferences.Set("VacantPlacesWest", VacantPlacesWest);
        Preferences.Set(Constants.EnableEntriesNorth, EnableEntriesNorth);
        Preferences.Set(Constants.EntriesNorth, Math.Clamp(EntriesNorth, 0, 13));
    }
}