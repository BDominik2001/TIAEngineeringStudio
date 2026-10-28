using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TiaStudio.Desktop.ViewModels;

namespace TiaStudio.Desktop
{
    /// <summary>Parancssori kapcsolók: --no-splash, --screenshot &lt;mappa&gt; [--demo &lt;gépmodell.json&gt;].</summary>
    internal sealed class StartupOptions
    {
        public bool NoSplash { get; private set; }

        public string ScreenshotDirectory { get; private set; }

        public string DemoProject { get; private set; }

        public static StartupOptions Parse(string[] args)
        {
            StartupOptions options = new StartupOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--no-splash":
                        options.NoSplash = true;
                        break;
                    case "--screenshot" when i + 1 < args.Length:
                        options.ScreenshotDirectory = Path.GetFullPath(args[++i]);
                        break;
                    case "--demo" when i + 1 < args.Length:
                        options.DemoProject = Path.GetFullPath(args[++i]);
                        break;
                }
            }
            return options;
        }
    }

    /// <summary>
    /// Dokumentációs / CI képernyőképek: a nyitókép két fázisa és a főablak összes szekciója PNG-be,
    /// ideiglenes sandbox-, kimeneti- és könyvtármappával (a felhasználó adatai nem változnak).
    /// TIA Portalt nem indít; a demó a validálás–ellenőrzés–generálás lépésekig jut.
    /// </summary>
    internal static class ScreenshotMode
    {
        private const int Width = 1366;
        private const int Height = 840;

        public static void Run(App app, StartupOptions options)
        {
            DispatcherTimer safety = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            safety.Tick += (s, e) => app.Shutdown(3);
            safety.Start();

            Directory.CreateDirectory(options.ScreenshotDirectory);
            SplashWindow splash = new SplashWindow();
            splash.Show();
            splash.ShowFrame(TimeSpan.FromSeconds(1.2));
            Pump(300);
            Capture(splash, splash.Width, splash.Height, Path.Combine(options.ScreenshotDirectory, "00-splash-logo.png"));
            splash.ShowFrame(TimeSpan.FromSeconds(3.2));
            Pump(300);
            Capture(splash, splash.Width, splash.Height, Path.Combine(options.ScreenshotDirectory, "01-splash.png"));
            splash.Close();

            string temp = Path.Combine(Path.GetTempPath(), "tes-screens-" + Guid.NewGuid().ToString("N"));
            (MainWindow window, MainViewModel viewModel) = App.BuildMainWindow(null, settings =>
            {
                settings.SandboxRoot = Path.Combine(temp, "sandbox");
                settings.OutputRoot = Path.Combine(temp, "generated");
                settings.LibraryRoot = Path.Combine(temp, "libraries");
                settings.LogDirectory = Path.Combine(temp, "logs");
                settings.JobDirectory = Path.Combine(temp, "jobs");
            });
            if (window == null)
            {
                app.Shutdown(2);
                return;
            }
            window.Width = Width;
            window.Height = Height;
            app.MainWindow = window;
            window.Show();
            Pump(500);

            viewModel.Library.NewLibraryName = "Motor_Library";
            viewModel.Library.NewLibraryDescription = "Motor- és szelepvezérlő blokkok";
            Execute(viewModel.Library.CreateLibraryCommand);
            Capture(window, Width, Height, Path.Combine(options.ScreenshotDirectory, "02-start.png"));

            if (options.DemoProject != null)
            {
                viewModel.LoadProjectFile(options.DemoProject);
                Execute(viewModel.ValidateCommand);
                WaitIdle(viewModel);
                Execute(viewModel.CheckCommand);
                WaitIdle(viewModel);
                Execute(viewModel.GenerateCommand);
                WaitIdle(viewModel);
                if (viewModel.GeneratedFiles.Count > 0)
                {
                    viewModel.SelectedFile = viewModel.GeneratedFiles[viewModel.GeneratedFiles.Count - 1];
                }
            }

            string[] names = { "start-workflow", "model", "validate", "review", "tia", "library", "log" };
            for (int section = 0; section < names.Length; section++)
            {
                viewModel.SelectedSection = section;
                Pump(400);
                Capture(window, Width, Height, Path.Combine(options.ScreenshotDirectory, (section + 3).ToString("00") + "-" + names[section] + ".png"));
            }

            window.Close();
            try
            {
                Directory.Delete(temp, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            app.Shutdown(0);
        }

        public static void Fail(App app, StartupOptions options, Exception ex)
        {
            Directory.CreateDirectory(options.ScreenshotDirectory);
            File.WriteAllText(Path.Combine(options.ScreenshotDirectory, "error.txt"), ex.ToString());
            app.Shutdown(4);
        }

        private static void Execute(System.Windows.Input.ICommand command)
        {
            Pump(50);
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
            Pump(50);
        }

        private static void WaitIdle(MainViewModel viewModel)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (viewModel.IsBusy && watch.Elapsed < TimeSpan.FromSeconds(45))
            {
                Pump(50);
            }
        }

        /// <summary>A gyökérvizuált a kért méretre rendezi (a CI-képernyő kisebb lehet), majd PNG-be rendereli.</summary>
        private static void Capture(Window window, double width, double height, string path)
        {
            FrameworkElement root = VisualTreeHelper.GetChildrenCount(window) > 0
                ? (FrameworkElement)VisualTreeHelper.GetChild(window, 0)
                : window;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        private static void Pump(int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            do
            {
                DispatcherFrame frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new DispatcherOperationCallback(f =>
                    {
                        ((DispatcherFrame)f).Continue = false;
                        return null;
                    }),
                    frame);
                Dispatcher.PushFrame(frame);
                Thread.Sleep(15);
            }
            while (watch.ElapsedMilliseconds < milliseconds);
        }
    }
}
