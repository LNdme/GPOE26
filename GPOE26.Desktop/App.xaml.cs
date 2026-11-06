namespace GPOE26.Desktop;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "GPOE26", MinimumWidth = 420, MinimumHeight = 600 };
}
