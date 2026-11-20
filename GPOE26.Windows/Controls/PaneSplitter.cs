using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace GPOE26.Desktop.Controls;

/// <summary>
/// Poignée entre deux panneaux : glisser à la souris ou au doigt, flèches ← → au clavier (Maj : pas de 40 px),
/// double-clic pour revenir à la largeur par défaut. Elle redimensionne un panneau latéral (menu ou tuteur) ;
/// la zone de lecture, au milieu, prend toujours le reste de la place.
/// </summary>
public sealed class PaneSplitter : Grid
{
    private readonly Border _line = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
    private double _startX, _startWidth;
    private bool _dragging;

    public PaneSplitter()
    {
        Width = 6;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Children.Add(_line);
        _line.SetValue(Border.BackgroundProperty, Application.Current.Resources["DividerStrokeColorDefaultBrush"]);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        AutomationProperties.SetName(this, "Redimensionner le panneau");

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => Finish();
        DoubleTapped += (_, _) => { Target?.SetValue(ColumnDefinition.WidthProperty, new GridLength(DefaultWidth)); Committed?.Invoke(this, EventArgs.Empty); };
        KeyDown += OnKey;
        PointerEntered += (_, _) => _line.Width = 3;
        PointerExited += (_, _) => { if (!_dragging) _line.Width = 1; };
    }

    /// <summary>Colonne du panneau latéral redimensionné.</summary>
    public ColumnDefinition? Target { get; set; }

    /// <summary>Le panneau est à droite de la poignée (tuteur) : tirer vers la gauche l'élargit.</summary>
    public bool TargetIsAfter { get; set; }

    public double MinPaneWidth { get; set; } = 200;
    public double MaxPaneWidth { get; set; } = 640;
    public double DefaultWidth { get; set; } = 280;

    /// <summary>Fin d'un glissement (ou d'un réglage au clavier) : la disposition peut être enregistrée.</summary>
    public event EventHandler? Committed;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Target is null) return;
        _dragging = CapturePointer(e.Pointer);
        _startX = e.GetCurrentPoint(null).Position.X;
        _startWidth = Target.ActualWidth;
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || Target is null) return;
        var delta = e.GetCurrentPoint(null).Position.X - _startX;
        Resize(_startWidth + (TargetIsAfter ? -delta : delta));
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleasePointerCapture(e.Pointer);
        Finish();
    }

    private void Finish()
    {
        if (!_dragging) return;
        _dragging = false;
        _line.Width = 1;
        Committed?.Invoke(this, EventArgs.Empty);
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (Target is null || e.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        var step = (shift ? 40 : 10) * (e.Key == VirtualKey.Right ? 1 : -1) * (TargetIsAfter ? -1 : 1);
        Resize(Target.ActualWidth + step);
        Committed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Resize(double width) =>
        Target!.Width = new GridLength(Math.Clamp(width, MinPaneWidth, MaxPaneWidth));
}
