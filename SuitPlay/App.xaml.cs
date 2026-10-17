using Calculator;
using Serilog;

namespace SuitPlay;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        Task.Run(async () => await CopyAllAssetsAsync());
    }
    
    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = new Window(new AppShell()) { Title = "SuitPlay — combinaisons de couleur" };

#if WINDOWS
        var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
        var screenHeight = displayInfo.Height / displayInfo.Density;
        window.Width = 900;
        window.Height = Math.Max(600, Math.Min(1100, screenHeight - 80));

        window.X = (displayInfo.Width / displayInfo.Density - window.Width) / 2;
        window.Y = (displayInfo.Height / displayInfo.Density - window.Height) / 2;
#endif
        return window;
    }
    
    private static async Task CopyAllAssetsAsync()
    {
        try
        {
            var sharedLibraryAssembly = typeof(Calculate).Assembly;
            await Utils.CopyEmbeddedFolderAsync(sharedLibraryAssembly, "Calculator.etalons_suitplay");
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to copy embedded folder: {Exception}", ex);
        }
    }

    
}