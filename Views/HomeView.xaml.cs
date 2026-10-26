using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Whitebox.Views;

public partial class HomeView : UserControl
{
    // "Your PC, but ___." — the highlighted word cycles.
    private static readonly string[] Words = ["fresher", "faster", "lighter", "calmer", "tidier"];

    private readonly DispatcherTimer _wordTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };
    private int _word;
    private bool _introPlayed;

    public HomeView()
    {
        InitializeComponent();
        _wordTimer.Tick += (_, _) => NextWord();
        Loaded += (_, _) =>
        {
            _wordTimer.Start();
            if (_introPlayed) return;
            _introPlayed = true;
            PlayIntro();
        };
        Unloaded += (_, _) => _wordTimer.Stop();
    }

    /// <summary>Hero copy rises in, one line after another.</summary>
    private void PlayIntro()
    {
        int i = 0;
        foreach (UIElement child in HeroText.Children)
        {
            var shift = new TranslateTransform(0, 22);
            child.RenderTransform = shift;
            child.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(110 * i++);
            child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = begin });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(560))
            {
                BeginTime = begin,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    /// <summary>Drops the current word out, swaps it, and bounces the next one in.</summary>
    private void NextWord()
    {
        if (!SwapWord.IsVisible) return;
        var fallOut = new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fallOut.Completed += (_, _) =>
        {
            _word = (_word + 1) % Words.Length;
            SwapWord.Text = Words[_word];
            SwapShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
            });
            SwapWord.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        };
        SwapShift.BeginAnimation(TranslateTransform.YProperty, fallOut);
        SwapWord.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(170)));
    }
}
