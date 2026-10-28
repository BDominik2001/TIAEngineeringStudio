using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TiaStudio.Desktop
{
    /// <summary>
    /// Nyitóképernyő: a logó megjelenik és a funkcióblokk "kirajzolódik", majd a név és az alcím
    /// beúszik; az Outro finoman elhalványítja, ezután indul a főablak. Ha a Windows animációi ki
    /// vannak kapcsolva (SystemParameters.ClientAreaAnimation), a sorozat gyorsítva fut.
    /// </summary>
    public partial class SplashWindow : Window
    {
        private Storyboard intro;

        public SplashWindow()
        {
            InitializeComponent();
        }

        private double SpeedRatio => SystemParameters.ClientAreaAnimation ? 1.0 : 4.0;

        public void PlayIntro(Action completed)
        {
            intro = ((Storyboard)FindResource("Intro")).Clone();
            intro.SpeedRatio = SpeedRatio;
            intro.Completed += (s, e) => completed?.Invoke();
            intro.Begin(this, true);
        }

        public void PlayOutro(Action completed)
        {
            Storyboard outro = ((Storyboard)FindResource("Outro")).Clone();
            outro.SpeedRatio = SpeedRatio;
            outro.Completed += (s, e) => completed?.Invoke();
            outro.Begin(this, true);
        }

        /// <summary>Képernyőkép-módhoz: az intro egy adott pillanata, megállítva.</summary>
        public void ShowFrame(TimeSpan at)
        {
            intro?.Stop(this);
            intro = ((Storyboard)FindResource("Intro")).Clone();
            intro.Begin(this, true);
            intro.SeekAlignedToLastTick(this, at, TimeSeekOrigin.BeginTime);
            intro.Pause(this);
        }
    }
}
