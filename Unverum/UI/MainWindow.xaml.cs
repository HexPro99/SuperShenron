using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Documents;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using System.Net.Http;
using System.Windows.Media;
using Unverum.UI;
using System.Windows.Controls.Primitives;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Windows.Input;

namespace Unverum
{
    public partial class MainWindow : Window
    {
        public string version;
        // Separated from Global.config so that order is updated when datagrid is modified
        public List<string> exes;
        private FileSystemWatcher ModsWatcher;
        private FlowDocument defaultFlow = new FlowDocument();
        private string defaultText = "SUPER SHENRON is here to help out with all your Dragon Ball FighterZ mods, and keeps Shenron up to date for you!\n\n" +
            "(Right Click Row > Fetch Metadata and confirm the GameBanana URL of the mod to fetch metadata to show here.)";
        private const string PreviewUri = "pack://application:,,,/SuperShenron;component/Assets/SuperShenronPreview.png";
        public bool Busy { get; private set; }

        private GameConfig Cfg => Global.config.Configs[Global.Game];

        private void FitToScreen()
        {
            const double designWidth = 1280, designHeight = 750;
            var area = SystemParameters.WorkArea;
            var chromeWidth = 16.0;
            var chromeHeight = SystemParameters.WindowCaptionHeight + 16.0;
            var scale = Math.Min(1.0, Math.Min((area.Width - chromeWidth) / designWidth, (area.Height - chromeHeight) / designHeight));
            if (scale < 1.0)
            {
                RootGrid.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                MinWidth = designWidth * scale + chromeWidth;
                MinHeight = designHeight * scale + chromeHeight;
            }
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
        }

        public MainWindow()
        {
            InitializeComponent();
            Global.logger = new Logger(ConsoleWindow);
            Global.config = new();

            // Get Version Number
            version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            Global.logger.WriteLine($"Launched SUPER SHENRON v{version}!", LoggerType.Info);
            if (File.Exists($@"{Global.assemblyLocation}{Global.s}Config.json"))
            {
                try
                {
                    var configString = File.ReadAllText($@"{Global.assemblyLocation}{Global.s}Config.json");
                    Global.config = JsonSerializer.Deserialize<Config>(configString) ?? new Config();
                }
                catch (Exception e)
                {
                    Global.logger.WriteLine(e.Message, LoggerType.Error);
                    Global.config = new();
                }
            }

            // Last saved windows settings
            if (Global.config.Height != null && Global.config.Height >= MinHeight)
                Height = (double)Global.config.Height;
            if (Global.config.Width != null && Global.config.Width >= MinWidth)
                Width = (double)Global.config.Width;
            FitToScreen();
            if (Global.config.Maximized)
                WindowState = WindowState.Maximized;
            if (Global.config.TopGridHeight != null)
                MainGrid.RowDefinitions[1].Height = new GridLength((double)Global.config.TopGridHeight, GridUnitType.Star);
            if (Global.config.BottomGridHeight != null)
                MainGrid.RowDefinitions[3].Height = new GridLength((double)Global.config.BottomGridHeight, GridUnitType.Star);
            if (Global.config.LeftGridWidth != null)
                MiddleGrid.ColumnDefinitions[0].Width = new GridLength((double)Global.config.LeftGridWidth, GridUnitType.Star);
            if (Global.config.RightGridWidth != null)
                MiddleGrid.ColumnDefinitions[2].Width = new GridLength((double)Global.config.RightGridWidth, GridUnitType.Star);

            Global.config.CurrentGame = Global.Game;
            if (Global.config.Configs == null)
                Global.config.Configs = new();
            if (!Global.config.Configs.ContainsKey(Global.Game))
                Global.config.Configs.Add(Global.Game, new());
            var cfg = Cfg;

            if (String.IsNullOrEmpty(cfg.CurrentLoadout))
                cfg.CurrentLoadout = "Default";
            if (cfg.Loadouts == null)
                cfg.Loadouts = new();
            if (!cfg.Loadouts.ContainsKey(cfg.CurrentLoadout) || cfg.Loadouts[cfg.CurrentLoadout] == null)
            {
                if (cfg.ModList != null && cfg.CurrentLoadout == "Default")
                {
                    cfg.Loadouts[cfg.CurrentLoadout] = cfg.ModList;
                    cfg.ModList = null;
                }
                else
                    cfg.Loadouts[cfg.CurrentLoadout] = new();
            }
            Global.ModList = cfg.Loadouts[cfg.CurrentLoadout];

            Global.LoadoutItems = new ObservableCollection<String>(cfg.Loadouts.Keys);

            LoadoutsBox.ItemsSource = Global.LoadoutItems;
            LoadoutsBox.SelectedItem = cfg.CurrentLoadout;

            if (!IsSetUp())
                LaunchButton.IsEnabled = false;

            Directory.CreateDirectory($@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.Game}");

            // Watch mods folder to detect
            ModsWatcher = new FileSystemWatcher($@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.Game}");
            ModsWatcher.Created += OnModified;
            ModsWatcher.Deleted += OnModified;
            ModsWatcher.Renamed += OnModified;

            RefreshAll();
            Refresh();
            ModsWatcher.EnableRaisingEvents = true;

            defaultFlow.Blocks.Add(ConvertToFlowParagraph(defaultText));
            DescriptionWindow.Document = defaultFlow;
            var bitmap = new BitmapImage(new Uri(PreviewUri));
            Preview.Source = bitmap;
            PreviewBG.Source = null;

            Global.logger.WriteLine("Checking for updates...", LoggerType.Info);
            SetBusy(true);
            App.Current.Dispatcher.Invoke(() =>
            {
                ModUpdater.CheckForUpdates($"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.Game}", this);
            });
        }

        public void SetBusy(bool busy)
        {
            Busy = busy;
            ModGrid.IsEnabled = !busy;
            ConfigButton.IsEnabled = !busy;
            LaunchButton.IsEnabled = !busy;
            OpenModsButton.IsEnabled = !busy;
            UpdateButton.IsEnabled = !busy;
            EditLoadoutsButton.IsEnabled = !busy;
            LoadoutsBox.IsEnabled = !busy;
            ModGridSearchButton.IsEnabled = !busy;
            ShenronButton.IsEnabled = !busy;
        }

        private bool IsSetUp()
        {
            var cfg = Cfg;
            return !String.IsNullOrEmpty(cfg.ModsFolder) && !String.IsNullOrEmpty(cfg.Launcher) && File.Exists(cfg.Launcher);
        }

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            ShenronUpdater.StartPeriodicChecks(this);
            if (!IsSetUp())
            {
                Global.logger.WriteLine("Looking for Dragon Ball FighterZ...", LoggerType.Info);
                var ok = await Task.Run(() => Setup.DBFZ());
                if (ok)
                {
                    if (!Busy)
                        LaunchButton.IsEnabled = true;
                }
                else
                    Global.logger.WriteLine("Please click Setup before starting!", LoggerType.Warning);
            }
        }

        private void OnModified(object sender, FileSystemEventArgs e)
        {
            Refresh();
            Global.UpdateConfig();
            // Bring window to front after download is done
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                Activate();
            });
        }

        private async void Refresh()
        {
            var currentModDirectory = $@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}";
            // Add new folders found in Mods to the ModList
            foreach (var mod in Directory.GetDirectories(currentModDirectory))
            {
                if (Global.ModList.ToList().Where(x => x.name == Path.GetFileName(mod)).Count() == 0)
                {
                    Mod m = new Mod();
                    m.name = Path.GetFileName(mod);
                    m.enabled = false;
                    App.Current.Dispatcher.Invoke((Action)delegate
                    {
                        Global.ModList.Add(m);
                    });
                    Global.logger.WriteLine($"Added {Path.GetFileName(mod)}", LoggerType.Info);
                }
            }
            // Remove deleted folders that are still in the ModList
            foreach (var mod in Global.ModList.ToList())
            {
                if (!Directory.GetDirectories(currentModDirectory).ToList().Select(x => Path.GetFileName(x)).Contains(mod.name))
                {
                    App.Current.Dispatcher.Invoke((Action)delegate
                    {
                        Global.ModList.Remove(mod);
                    });
                    Global.logger.WriteLine($"Deleted {mod.name}", LoggerType.Info);
                    continue;
                }
                // Update all paks found
                if (mod.paks == null)
                    mod.paks = new();
                foreach (var file in Directory.GetFiles($"{currentModDirectory}{Global.s}{mod.name}", "*.*", SearchOption.AllDirectories))
                {
                    if (Path.GetExtension(file).Equals(".pak", StringComparison.InvariantCultureIgnoreCase)
                        && !mod.paks.ContainsKey(file))
                        mod.paks.Add(file, true); // Enable all paks when first added
                }
                // Remove all paks that no longer exist
                foreach (var pak in mod.paks.Keys)
                {
                    if (!File.Exists(pak))
                        mod.paks.Remove(pak);
                }
            }

            await Task.Run(() =>
            {
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    ModGrid.ItemsSource = Global.ModList;
                    Stats.Text = $"{Global.ModList.Count} mods • {Directory.GetFiles($@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}", "*", SearchOption.AllDirectories).Length.ToString("N0")} files • " +
                    $"{StringConverters.FormatSize(new DirectoryInfo($@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}").GetDirectorySize())} • v{version}";
                });
            });
            Global.config.Configs[Global.config.CurrentGame].ModList = Global.ModList;
            Global.logger.WriteLine("Refreshed!", LoggerType.Info);
        }
        private void RefreshAll()
        {
            var currentModDirectory = $@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}";
            var currlist = Global.config.Configs[Global.config.CurrentGame].CurrentLoadout;
            foreach (var list in Global.config.Configs[Global.config.CurrentGame].Loadouts)
            {
                // Add new folders found in Mods to the ModList
                foreach (var mod in Directory.GetDirectories(currentModDirectory))
                {
                    if (list.Value.ToList().Where(x => x.name == Path.GetFileName(mod)).Count() == 0)
                    {
                        Mod m = new Mod();
                        m.name = Path.GetFileName(mod);
                        m.enabled = false;
                        App.Current.Dispatcher.Invoke((Action)delegate
                        {
                            list.Value.Add(m);
                        });
                    }
                }
                // Remove deleted folders that are still in the ModList
                foreach (var mod in list.Value.ToList())
                {
                    if (!Directory.GetDirectories(currentModDirectory).ToList().Select(x => Path.GetFileName(x)).Contains(mod.name))
                    {
                        App.Current.Dispatcher.Invoke((Action)delegate
                        {
                            list.Value.Remove(mod);
                        });
                        continue;
                    }
                    // Update all paks found
                    if (mod.paks == null)
                        mod.paks = new();
                    foreach (var file in Directory.GetFiles($"{currentModDirectory}{Global.s}{mod.name}", "*.*", SearchOption.AllDirectories))
                    {
                        if (Path.GetExtension(file).Equals(".pak", StringComparison.InvariantCultureIgnoreCase)
                            && !mod.paks.ContainsKey(file))
                            mod.paks.Add(file, true); // Enable all paks when first added
                    }
                    // Remove all paks that no longer exist
                    foreach (var pak in mod.paks.Keys)
                    {
                        if (!File.Exists(pak))
                            mod.paks.Remove(pak);
                    }
                }

                Global.logger.WriteLine($"Total {list.Value.Count} mods in {list.Key}", LoggerType.Info);
            }

            Global.config.Configs[Global.config.CurrentGame].CurrentLoadout = currlist;
            Global.config.Configs[Global.config.CurrentGame].ModList = Global.ModList;
        }

        // Events for Enabled checkboxes
        private void OnChecked(object sender, RoutedEventArgs e)
        {
            var checkBox = e.OriginalSource as CheckBox;

            Mod mod = checkBox?.DataContext as Mod;

            if (mod != null)
            {
                mod.enabled = true;
                List<Mod> temp = Global.config.Configs[Global.config.CurrentGame].ModList.ToList();
                foreach (var m in temp)
                {
                    if (m.name == mod.name)
                        m.enabled = true;
                }
                Global.config.Configs[Global.config.CurrentGame].ModList = new ObservableCollection<Mod>(temp);
                Global.UpdateConfig();
            }
        }
        private void OnUnchecked(object sender, RoutedEventArgs e)
        {
            var checkBox = e.OriginalSource as CheckBox;

            Mod mod = checkBox?.DataContext as Mod;

            if (mod != null)
            {
                mod.enabled = false;
                List<Mod> temp = Global.config.Configs[Global.config.CurrentGame].ModList.ToList();
                foreach (var m in temp)
                {
                    if (m.name == mod.name)
                        m.enabled = false;
                }
                Global.config.Configs[Global.config.CurrentGame].ModList = new ObservableCollection<Mod>(temp);
                Global.UpdateConfig();
            }
        }
        // Triggered when priority is switched on drag and dropped
        private void ModGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            Global.UpdateConfig();
        }

        private async void Setup_Click(object sender, RoutedEventArgs e)
        {
            ConfigButton.IsEnabled = false;
            await Task.Run(() =>
            {
                var cfg = Cfg;
                if (!String.IsNullOrEmpty(cfg.ModsFolder)
                    || !String.IsNullOrEmpty(cfg.Launcher) && File.Exists(cfg.Launcher))
                {
                    var dialogResult = MessageBox.Show($@"Setup again?", $@"Notification", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (dialogResult == MessageBoxResult.No)
                        return;
                }
                if (Setup.DBFZ())
                {
                    Dispatcher.Invoke(() =>
                    {
                        LaunchButton.IsEnabled = true;
                    });
                }
            });
            ConfigButton.IsEnabled = true;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Ensure the destination directory exists
            Directory.CreateDirectory(destinationDir);

            // Copy all files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            // Recursively copy all subdirectories
            foreach (var subdir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subdir));
                CopyDirectory(subdir, destSubDir);
            }
        }
        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            var cfg = Cfg;
            // Build Mod Loadout
            if (cfg.ModsFolder != null)
            {
                SetBusy(true);
                Refresh();
                Regex regex = new Regex(@"(^~*[a-z]$|^--Base--$)");
                var manuallyInstalledMods = Directory.Exists(cfg.ModsFolder)
                    ? Directory.GetDirectories(cfg.ModsFolder).Where(folder => !regex.IsMatch(Path.GetFileName(folder))).ToList()
                    : new List<string>();
                if (manuallyInstalledMods.Count() > 0)
                {
                    var dialogResult = MessageBox.Show($@"SUPER SHENRON detected manually installed mods in {cfg.ModsFolder}. " +
                        $@"Would you like to copy over these mods to SUPER SHENRON before it DELETES them?", $@"Notification", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (dialogResult == MessageBoxResult.Yes)
                    {
                        foreach (var manuallyInstalledMod in manuallyInstalledMods)
                            CopyDirectory(manuallyInstalledMod,
                                Path.Combine(Global.assemblyLocation, "Mods", Global.Game, Path.GetFileName(manuallyInstalledMod)));
                    }
                }
                Directory.CreateDirectory(cfg.ModsFolder);
                Global.logger.WriteLine($"Building loadout for {Global.Game}", LoggerType.Info);
                var built = await Build(cfg.ModsFolder);
                SetBusy(false);
                if (!built)
                {
                    Global.logger.WriteLine($"Failed to build loadout, not building and launching", LoggerType.Error);
                    return;
                }
            }
            else
            {
                Global.logger.WriteLine("Please click Setup before starting!", LoggerType.Warning);
                return;
            }
            // Launch game
            if (cfg.Launcher != null && File.Exists(cfg.Launcher))
            {
                var path = cfg.Launcher;
                try
                {
                    Global.logger.WriteLine($"Launching {path}", LoggerType.Info);
                    var ps = new ProcessStartInfo(path)
                    {
                        WorkingDirectory = Path.GetDirectoryName(path),
                        UseShellExecute = true,
                        Verb = "open"
                    };
                    Process.Start(ps);
                }
                catch (Exception ex)
                {
                    Global.logger.WriteLine($"Couldn't launch {path} ({ex.Message})", LoggerType.Error);
                }
            }
            else
                Global.logger.WriteLine($"Please click Setup before starting!", LoggerType.Warning);
        }
        private void GameBanana_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ps = new ProcessStartInfo("https://gamebanana.com/games/6246")
                {
                    UseShellExecute = true,
                    Verb = "open"
                };
                Process.Start(ps);
            }
            catch (Exception ex)
            {
                Global.logger.WriteLine($"Couldn't open up GameBanana ({ex.Message})", LoggerType.Error);
            }
        }
        private void Shenron_Click(object sender, RoutedEventArgs e)
        {
            new ShenronWindow { Owner = this }.ShowDialog();
        }
        private void About_Click(object sender, MouseButtonEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void ScrollToBottom(object sender, TextChangedEventArgs args)
        {
            ConsoleWindow.ScrollToEnd();
        }

        private void ModGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            if (element == null)
            {
                return;
            }

            if (ModGrid.SelectedItem == null)
                element.ContextMenu.Visibility = Visibility.Collapsed;
            else
                element.ContextMenu.Visibility = Visibility.Visible;
        }

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedMods = ModGrid.SelectedItems;
            var temp = new Mod[selectedMods.Count];
            selectedMods.CopyTo(temp, 0);
            foreach (var row in temp)
                if (row != null)
                {
                    var dialogResult = MessageBox.Show($@"Are you sure you want to delete {row.name}?" + Environment.NewLine + "This cannot be undone.", $@"Deleting {row.name}: Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (dialogResult == MessageBoxResult.Yes)
                    {
                        try
                        {
                            await Task.Run(() => Directory.Delete($@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}{Global.s}{row.name}", true));
                            Global.logger.WriteLine($@"Deleting {row.name}.", LoggerType.Info);
                            ShowMetadata(null);
                        }
                        catch (Exception ex)
                        {
                            Global.logger.WriteLine($@"Couldn't delete {row.name} ({ex.Message})", LoggerType.Error);
                        }
                    }
                }
        }

        private async Task<bool> Build(string path)
        {
            return await Task.Run(() =>
            {
                var cfg = Cfg;
                if (String.IsNullOrEmpty(cfg.Launcher) || !File.Exists(cfg.Launcher))
                {
                    Global.logger.WriteLine("Couldn't find the game's launcher, please click Setup", LoggerType.Error);
                    return false;
                }
                // Get other folders using the mods folder
                string SplashFolder = null;
                string MoviesFolder = null;
                string SoundsFolder = null;
                var ContentFolder = new DirectoryInfo(cfg.ModsFolder).Parent.Parent.FullName;
                if (Directory.Exists($"{ContentFolder}{Global.s}Splash"))
                    SplashFolder = $"{ContentFolder}{Global.s}Splash";
                if (Directory.Exists($"{ContentFolder}{Global.s}Movies"))
                    MoviesFolder = $"{ContentFolder}{Global.s}Movies";
                else if (Directory.Exists($"{ContentFolder}{Global.s}Binaries{Global.s}Movie"))
                    MoviesFolder = $"{ContentFolder}{Global.s}Binaries{Global.s}Movie";
                if (Directory.Exists($"{ContentFolder}{Global.s}Sound"))
                    SoundsFolder = $"{ContentFolder}{Global.s}Sound";
                else if (Directory.Exists($"{ContentFolder}{Global.s}CriWareData"))
                    SoundsFolder = $"{ContentFolder}{Global.s}CriWareData";
                bool? Patched = Setup.CheckPatch(cfg.Launcher);
                if (!ModLoader.Restart(path, MoviesFolder, SplashFolder, SoundsFolder))
                    return false;
                var mods = cfg.ModList.Where(x => x.enabled).ToList();
                mods.Reverse();

                ModLoader.Build(path, mods, Patched, MoviesFolder, SplashFolder, SoundsFolder);
                return true;
            });
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                Global.config.Height = RestoreBounds.Height;
                Global.config.Width = RestoreBounds.Width;
                Global.config.Maximized = true;
            }
            else
            {
                Global.config.Height = Height;
                Global.config.Width = Width;
                Global.config.Maximized = false;
            }
            Global.config.TopGridHeight = MainGrid.RowDefinitions[1].Height.Value;
            Global.config.BottomGridHeight = MainGrid.RowDefinitions[3].Height.Value;
            Global.config.LeftGridWidth = MiddleGrid.ColumnDefinitions[0].Width.Value;
            Global.config.RightGridWidth = MiddleGrid.ColumnDefinitions[2].Width.Value;
            Global.UpdateConfig();
            ShenronUpdater.StopPeriodicChecks();
            Application.Current.Shutdown();
        }

        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedMods = ModGrid.SelectedItems;
            var temp = new Mod[selectedMods.Count];
            selectedMods.CopyTo(temp, 0);
            foreach (var row in temp)
                if (row != null)
                {
                    var folderName = $@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}{Global.s}{row.name}";
                    if (Directory.Exists(folderName))
                    {
                        try
                        {
                            Process process = Process.Start("explorer.exe", folderName);
                            Global.logger.WriteLine($@"Opened {folderName}.", LoggerType.Info);
                        }
                        catch (Exception ex)
                        {
                            Global.logger.WriteLine($@"Couldn't open {folderName}. ({ex.Message})", LoggerType.Error);
                        }
                    }
                }
        }
        private void EditItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedMods = ModGrid.SelectedItems;
            var temp = new Mod[selectedMods.Count];
            selectedMods.CopyTo(temp, 0);

            // Stop refreshing while renaming folders
            ModsWatcher.EnableRaisingEvents = false;
            foreach (var row in temp)
                if (row != null)
                {
                    EditWindow ew = new EditWindow(row.name, true);
                    ew.ShowDialog();
                }
            ModsWatcher.EnableRaisingEvents = true;
            Global.UpdateConfig();
            ModGrid.Items.Refresh();
        }
        private void ConfigurePaksItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedMods = ModGrid.SelectedItems;
            var temp = new Mod[selectedMods.Count];
            selectedMods.CopyTo(temp, 0);
            bool edited = false;
            foreach (var row in temp)
                if (row != null)
                {
                    ConfigurePaksWindow cpw = new ConfigurePaksWindow(row);
                    cpw.ShowDialog();
                    var index = Global.ModList.IndexOf(row);
                    Global.ModList[index] = cpw._mod;
                    Global.UpdateConfig();
                }
        }
        private void FetchItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedMods = ModGrid.SelectedItems;
            var temp = new Mod[selectedMods.Count];
            selectedMods.CopyTo(temp, 0);
            foreach (var row in temp)
                if (row != null)
                {
                    FetchWindow fw = new FetchWindow(row);
                    fw.ShowDialog();
                    if (fw.success)
                        ShowMetadata(row.name);
                }
        }
        private void Add_Enter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Handled = true;
                e.Effects = DragDropEffects.Move;
                DropBox.Visibility = Visibility.Visible;
            }
        }
        private void Add_Leave(object sender, DragEventArgs e)
        {
            e.Handled = true;
            DropBox.Visibility = Visibility.Collapsed;
        }
        private void Add_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] fileList = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                CreateMod(fileList);
            }
            DropBox.Visibility = Visibility.Collapsed;
        }
        private void CreateMod(string[] files)
        {
            var nameWindow = new EditWindow(null, true);
            nameWindow.ShowDialog();
            if (nameWindow.directory != null)
            {
                Directory.CreateDirectory(nameWindow.directory);
                string defaultSig = null;
                if (Directory.Exists(Global.config.Configs[Global.config.CurrentGame].ModsFolder))
                {
                    var sigs = Directory.GetFiles(Path.GetDirectoryName(Global.config.Configs[Global.config.CurrentGame].ModsFolder), "*.sig", SearchOption.TopDirectoryOnly);
                    if (sigs.Length > 0)
                        defaultSig = sigs[0];
                }
                foreach (var file in files)
                {
                    // Get the file attributes for file or directory
                    FileAttributes attr = File.GetAttributes(file);

                    if (attr.HasFlag(FileAttributes.Directory))
                    {
                        foreach (string path in Directory.GetFiles(file, "*.*", SearchOption.AllDirectories))
                        {
                            var newPath = path.Replace(file, $"{nameWindow.directory}{Global.s}{Path.GetFileName(file)}");
                            Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                            File.Copy(path, newPath, true);
                            if (Path.GetExtension(path).Equals(".pak", StringComparison.InvariantCultureIgnoreCase) && defaultSig != null)
                            {
                                var sig = Path.ChangeExtension(path, ".sig");
                                var newPathSig = Path.ChangeExtension(newPath, ".sig");
                                // Check if mod folder has corresponding .sig
                                if (File.Exists(sig))
                                    File.Copy(sig, newPathSig, true);
                                // Otherwise copy over original game's .sig
                                else if (File.Exists(defaultSig))
                                    File.Copy(defaultSig, newPathSig, true);
                            }
                        }
                    }
                    else
                    {
                        var newPath = $"{nameWindow.directory}{Global.s}{Path.GetFileName(file)}";
                        File.Copy(file, newPath, true);
                        if (Path.GetExtension(file).Equals(".pak", StringComparison.InvariantCultureIgnoreCase) && defaultSig != null)
                        {
                            var sig = Path.ChangeExtension(file, ".sig");
                            var newPathSig = Path.ChangeExtension(newPath, ".sig");
                            // Check if mod folder has corresponding .sig
                            if (File.Exists(sig))
                                File.Copy(sig, newPathSig, true);
                            // Otherwise copy over original game's .sig
                            else if (File.Exists(defaultSig))
                                File.Copy(defaultSig, newPathSig, true);
                        }
                    }
                }
            }
        }
        private void ModsFolder_Click(object sender, RoutedEventArgs e)
        {
            var choices = new List<Choice>();
            choices.Add(new Choice()
            {
                OptionText = "Create New Mod",
                OptionSubText = "Name a mod and choose the .pak file for it to use",
                Index = 0
            });
            choices.Add(new Choice()
            {
                OptionText = "Open Mods Folder",
                OptionSubText = "Drag or extract mod folders into this directory",
                Index = 1
            });
            var choice = new ChoiceWindow(choices);
            choice.ShowDialog();
            if (choice.choice != null && (int)choice.choice == 0)
            {
                var nameWindow = new EditWindow(null, true);
                nameWindow.ShowDialog();
                if (nameWindow.directory != null)
                {
                    OpenFileDialog dialog = new OpenFileDialog();
                    dialog.DefaultExt = ".pak";
                    dialog.Filter = "UE4 Package Files (*.pak)|*.pak";
                    dialog.Title = $"Select .pak to add in {Path.GetFileName(nameWindow.directory)}";
                    dialog.Multiselect = false;
                    dialog.ShowDialog();
                    if (!String.IsNullOrEmpty(dialog.FileName))
                    {
                        Directory.CreateDirectory(nameWindow.directory);
                        File.Copy(dialog.FileName, $"{nameWindow.directory}{Global.s}{Path.GetFileName(dialog.FileName)}", true);
                        if (Directory.Exists(Global.config.Configs[Global.config.CurrentGame].ModsFolder))
                        {
                            // Copy over sig if it exists
                            var sigs = Directory.GetFiles(Path.GetDirectoryName(Global.config.Configs[Global.config.CurrentGame].ModsFolder), "*.sig", SearchOption.TopDirectoryOnly);
                            if (sigs.Length > 0)
                                File.Copy(sigs[0], Path.ChangeExtension($"{nameWindow.directory}{Global.s}{Path.GetFileName(dialog.FileName)}", ".sig"), true);
                        }
                    }
                }
            }
            else if (choice.choice != null && (int)choice.choice == 1)
            {
                var folderName = $"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}";
                if (Directory.Exists(folderName))
                {
                    try
                    {
                        Process process = Process.Start("explorer.exe", folderName);
                        Global.logger.WriteLine($@"Opened {folderName}.", LoggerType.Info);
                    }
                    catch (Exception ex)
                    {
                        Global.logger.WriteLine($@"Couldn't open {folderName}. ({ex.Message})", LoggerType.Error);
                    }
                }
            }
        }
        private void Update_Click(object sender, RoutedEventArgs e)
        {
            Global.logger.WriteLine("Checking for updates...", LoggerType.Info);
            SetBusy(true);
            App.Current.Dispatcher.Invoke(() =>
            {
                ModUpdater.CheckForUpdates($"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.Game}", this);
            });
        }
        private Paragraph ConvertToFlowParagraph(string text)
        {
            var flowDocument = new FlowDocument();

            var regex = new Regex(@"(https?:\/\/[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var matches = regex.Matches(text).Cast<Match>().Select(m => m.Value).ToList();

            var paragraph = new Paragraph();
            flowDocument.Blocks.Add(paragraph);

            foreach (var segment in regex.Split(text))
            {
                if (matches.Contains(segment))
                {
                    var hyperlink = new Hyperlink(new Run(segment))
                    {
                        NavigateUri = new Uri(segment),
                    };

                    hyperlink.RequestNavigate += (sender, args) =>
                    {
                        var ps = new ProcessStartInfo(segment)
                        {
                            UseShellExecute = true,
                            Verb = "open"
                        };
                        Process.Start(ps);
                    };

                    paragraph.Inlines.Add(hyperlink);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(segment));
                }
            }

            return paragraph;
        }

        private void ShowMetadata(string mod)
        {
            if (mod == null || !File.Exists($"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}{Global.s}{mod}{Global.s}mod.json"))
            {
                DescriptionWindow.Document = defaultFlow;
                var bitmap = new BitmapImage(new Uri("pack://application:,,,/SuperShenron;component/Assets/SuperShenronPreview.png"));
                Preview.Source = bitmap;
                PreviewBG.Source = null;
            }
            else
            {
                FlowDocument descFlow = new FlowDocument();
                var metadataString = File.ReadAllText($"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}{Global.s}{mod}{Global.s}mod.json");
                Metadata metadata = JsonSerializer.Deserialize<Metadata>(metadataString);

                var para = new Paragraph();
                if (metadata.submitter != null)
                {
                    para.Inlines.Add($"Submitter: ");
                    if (metadata.avi != null && metadata.avi.ToString().Length > 0)
                    {
                        BitmapImage bm = new BitmapImage(metadata.avi);
                        Image image = new Image();
                        image.Source = bm;
                        image.Height = 35;
                        para.Inlines.Add(image);
                        para.Inlines.Add(" ");
                    }
                    if (metadata.upic != null && metadata.upic.ToString().Length > 0)
                    {
                        BitmapImage bm = new BitmapImage(metadata.upic);
                        Image image = new Image();
                        image.Source = bm;
                        image.Height = 25;
                        para.Inlines.Add(image);
                    }
                    else
                        para.Inlines.Add(metadata.submitter);
                    descFlow.Blocks.Add(para);
                }
                if (metadata.preview != null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = metadata.preview;
                    bitmap.EndInit();
                    Preview.Source = bitmap;
                    PreviewBG.Source = bitmap;
                }
                else
                {
                    var bitmap = new BitmapImage(new Uri("pack://application:,,,/SuperShenron;component/Assets/SuperShenronPreview.png"));
                    Preview.Source = bitmap;
                    PreviewBG.Source = null;
                }
                para = new Paragraph();
                para.Inlines.Add("Category: ");
                if (metadata.caticon != null && metadata.caticon.ToString().Length > 0)
                {
                    BitmapImage bm = new BitmapImage(metadata.caticon);
                    Image image = new Image();
                    image.Source = bm;
                    image.Width = 20;
                    para.Inlines.Add(image);
                }
                para.Inlines.Add($" {metadata.cat}");
                descFlow.Blocks.Add(para);
                var text = "";
                if (!String.IsNullOrEmpty(metadata.description))
                    text += $"Description: {metadata.description}\n\n";
                if (!String.IsNullOrEmpty(metadata.filedescription))
                    text += $"File Description: {metadata.filedescription}\n\n";
                if (metadata.homepage != null && metadata.homepage.ToString().Length > 0)
                    text += $"Home Page: {metadata.homepage}";
                var init = ConvertToFlowParagraph(text);
                descFlow.Blocks.Add(init);
                DescriptionWindow.Document = descFlow;
                var descriptionText = new TextRange(DescriptionWindow.Document.ContentStart, DescriptionWindow.Document.ContentEnd);
                descriptionText.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Center);
            }
        }
        private void ModGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Mod row = (Mod)ModGrid.SelectedItem;
            if (row != null)
                ShowMetadata(row.name);
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            var item = button.DataContext as GameBananaRecord;
            new ModDownloader().BrowserDownload(Global.Game, item);
        }
        private void AltDownload_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            var item = button.DataContext as GameBananaRecord;
            new AltLinkWindow(item.AlternateFileSources, item.Title, Global.Game, item.Link.AbsoluteUri).ShowDialog();
        }
        private void Homepage_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            var item = button.DataContext as GameBananaRecord;
            try
            {
                var ps = new ProcessStartInfo(item.Link.ToString())
                {
                    UseShellExecute = true,
                    Verb = "open"
                };
                Process.Start(ps);
            }
            catch (Exception ex)
            {
                Global.logger.WriteLine($"Couldn't open up {item.Link} ({ex.Message})", LoggerType.Error);
            }
        }
        private int imageCounter;
        private int imageCount;
        private FlowDocument ConvertToFlowDocument(string text)
        {
            var flowDocument = new FlowDocument();

            var regex = new Regex(@"(https?:\/\/[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var matches = regex.Matches(text).Cast<Match>().Select(m => m.Value).ToList();

            var paragraph = new Paragraph();
            flowDocument.Blocks.Add(paragraph);

            foreach (var segment in regex.Split(text))
            {
                if (matches.Contains(segment))
                {
                    var hyperlink = new Hyperlink(new Run(segment))
                    {
                        NavigateUri = new Uri(segment),
                    };

                    hyperlink.RequestNavigate += (sender, args) => Process.Start(segment);

                    paragraph.Inlines.Add(hyperlink);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(segment));
                }
            }

            return flowDocument;
        }
        private void MoreInfo_Click(object sender, RoutedEventArgs e)
        {
            HomepageButton.Content = $"{(TypeBox.SelectedValue as ComboBoxItem).Content.ToString().Trim().TrimEnd('s')} Page";
            Button button = sender as Button;
            var item = button.DataContext as GameBananaRecord;
            if (item.Compatible)
                DownloadButton.Visibility = Visibility.Visible;
            else
                DownloadButton.Visibility = Visibility.Collapsed;
            if (item.HasAltLinks)
                AltButton.Visibility = Visibility.Visible;
            else
                AltButton.Visibility = Visibility.Collapsed;
            DescPanel.DataContext = button.DataContext;
            MediaPanel.DataContext = button.DataContext;
            DescText.ScrollToHome();
            var text = "";
            text += item.ConvertedText;
            DescText.Document = ConvertToFlowDocument(text);
            ImageLeft.IsEnabled = true;
            ImageRight.IsEnabled = true;
            BigImageLeft.IsEnabled = true;
            BigImageRight.IsEnabled = true;
            imageCount = item.Media.Where(x => x.Type == "image").ToList().Count;
            imageCounter = 0;
            if (imageCount > 0)
            {
                Grid.SetColumnSpan(DescText, 1);
                ImagePanel.Visibility = Visibility.Visible;
                var image = new BitmapImage(new Uri($"{item.Media[imageCounter].Base}/{item.Media[imageCounter].File}"));
                Screenshot.Source = image;
                BigScreenshot.Source = image;
                CaptionText.Text = item.Media[imageCounter].Caption;
                BigCaptionText.Text = item.Media[imageCounter].Caption;
                if (!String.IsNullOrEmpty(CaptionText.Text))
                {
                    BigCaptionText.Visibility = Visibility.Visible;
                    CaptionText.Visibility = Visibility.Visible;
                }
                else
                {
                    BigCaptionText.Visibility = Visibility.Collapsed;
                    CaptionText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                Grid.SetColumnSpan(DescText, 2);
                ImagePanel.Visibility = Visibility.Collapsed;
            }
            if (imageCount == 1)
            {
                ImageLeft.IsEnabled = false;
                ImageRight.IsEnabled = false;
                BigImageLeft.IsEnabled = false;
                BigImageRight.IsEnabled = false;
            }

            DescPanel.Visibility = Visibility.Visible;
        }
        private void CloseDesc_Click(object sender, RoutedEventArgs e)
        {
            DescPanel.Visibility = Visibility.Collapsed;
        }
        private void CloseMedia_Click(object sender, RoutedEventArgs e)
        {
            MediaPanel.Visibility = Visibility.Collapsed;
        }

        private void Image_Click(object sender, RoutedEventArgs e)
        {
            MediaPanel.Visibility = Visibility.Visible;
        }

        private void ImageLeft_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            var item = button.DataContext as GameBananaRecord;
            if (--imageCounter == -1)
                imageCounter = imageCount - 1;
            var image = new BitmapImage(new Uri($"{item.Media[imageCounter].Base}/{item.Media[imageCounter].File}"));
            Screenshot.Source = image;
            CaptionText.Text = item.Media[imageCounter].Caption;
            BigScreenshot.Source = image;
            BigCaptionText.Text = item.Media[imageCounter].Caption;
            if (!String.IsNullOrEmpty(CaptionText.Text))
            {
                BigCaptionText.Visibility = Visibility.Visible;
                CaptionText.Visibility = Visibility.Visible;
            }
            else
            {
                BigCaptionText.Visibility = Visibility.Collapsed;
                CaptionText.Visibility = Visibility.Collapsed;
            }
        }

        private void ImageRight_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            var item = button.DataContext as GameBananaRecord;
            if (++imageCounter == imageCount)
                imageCounter = 0;
            var image = new BitmapImage(new Uri($"{item.Media[imageCounter].Base}/{item.Media[imageCounter].File}"));
            Screenshot.Source = image;
            CaptionText.Text = item.Media[imageCounter].Caption;
            BigScreenshot.Source = image;
            BigCaptionText.Text = item.Media[imageCounter].Caption;
            if (!String.IsNullOrEmpty(CaptionText.Text))
            {
                BigCaptionText.Visibility = Visibility.Visible;
                CaptionText.Visibility = Visibility.Visible;
            }
            else
            {
                BigCaptionText.Visibility = Visibility.Collapsed;
                CaptionText.Visibility = Visibility.Collapsed;
            }
        }
        private static bool selected = false;

        private static Dictionary<TypeFilter, List<GameBananaCategory>> cats = new();

        private static readonly List<GameBananaCategory> All = new GameBananaCategory[]
        {
            new GameBananaCategory()
            {
                Name = "All",
                ID = null
            }
        }.ToList();
        private static readonly List<GameBananaCategory> None = new GameBananaCategory[]
        {
            new GameBananaCategory()
            {
                Name = "- - -",
                ID = null
            }
        }.ToList();
        private void ShowBrowserError(string message)
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            BrowserRefreshButton.Visibility = Visibility.Visible;
            BrowserMessage.Text = message;
        }
        private static string DescribeHttpError(HttpRequestException ex)
        {
            switch (Regex.Match(ex.Message, @"\d+").Value)
            {
                case "443":
                    return "Your internet connection is down.";
                case "500":
                case "503":
                case "504":
                    return "GameBanana's servers are down.";
                default:
                    return ex.Message;
            }
        }
        private async void InitializeBrowser()
        {
            using (var httpClient = new HttpClient())
            {
                ErrorPanel.Visibility = Visibility.Collapsed;
                const string gameID = "6246";
                var types = new string[] { "Mod", "Wip", "Sound" };
                var counter = 0;
                double totalPages = 0;
                foreach (var type in types)
                {
                    var requestUrl = $"https://gamebanana.com/apiv4/{type}Category/ByGame?_aGameRowIds[]={gameID}&_sRecordSchema=Custom" +
                        "&_csvProperties=_idRow,_sName,_sProfileUrl,_sIconUrl,_idParentCategoryRow&_nPerpage=50";
                    string responseString = "";
                    try
                    {
                        var responseMessage = await httpClient.GetAsync(requestUrl);
                        responseString = await responseMessage.Content.ReadAsStringAsync();
                        responseString = Regex.Replace(responseString, @"""(\d+)""", @"$1");
                        var numRecords = responseMessage.GetHeader("X-GbApi-Metadata_nRecordCount");
                        if (numRecords != -1)
                        {
                            totalPages = Math.Ceiling(numRecords / 50);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        ShowBrowserError(DescribeHttpError(ex));
                        return;
                    }
                    catch (Exception ex)
                    {
                        ShowBrowserError(ex.Message);
                        return;
                    }
                    List<GameBananaCategory> response = new();
                    try
                    {
                        response = JsonSerializer.Deserialize<List<GameBananaCategory>>(responseString);
                    }
                    catch (Exception)
                    {
                        ShowBrowserError("Uh oh! Something went wrong while deserializing the categories...");
                        return;
                    }
                    cats[(TypeFilter)counter] = response;

                    // Make more requests if needed
                    if (totalPages > 1)
                    {
                        for (double i = 2; i <= totalPages; i++)
                        {
                            var requestUrlPage = $"{requestUrl}&_nPage={i}";
                            try
                            {
                                responseString = await httpClient.GetStringAsync(requestUrlPage);
                                responseString = Regex.Replace(responseString, @"""(\d+)""", @"$1");
                            }
                            catch (HttpRequestException ex)
                            {
                                ShowBrowserError(DescribeHttpError(ex));
                                return;
                            }
                            catch (Exception ex)
                            {
                                ShowBrowserError(ex.Message);
                                return;
                            }
                            try
                            {
                                response = JsonSerializer.Deserialize<List<GameBananaCategory>>(responseString);
                            }
                            catch (Exception)
                            {
                                ShowBrowserError("Uh oh! Something went wrong while deserializing the categories...");
                                return;
                            }
                            cats[(TypeFilter)counter] = cats[(TypeFilter)counter].Concat(response).ToList();
                        }
                    }
                    counter++;
                }
            }
            filterSelect = true;
            FilterBox.ItemsSource = FilterBoxList;
            SetRootCategories();
            FilterBox.SelectedIndex = 1;
            filterSelect = false;
            RefreshFilter();
            selected = true;
        }
        private void SetRootCategories()
        {
            var typeCats = cats[(TypeFilter)TypeBox.SelectedIndex];
            if (typeCats.Any(x => x.RootID == 0))
                CatBox.ItemsSource = All.Concat(typeCats.Where(x => x.RootID == 0).OrderBy(y => y.ID));
            else
                CatBox.ItemsSource = None;
            CatBox.SelectedIndex = 0;
            SetSubCategories();
        }
        private void SetSubCategories()
        {
            var typeCats = cats[(TypeFilter)TypeBox.SelectedIndex];
            var cat = (GameBananaCategory)CatBox.SelectedValue;
            if (cat != null && typeCats.Any(x => x.RootID == cat.ID))
                SubCatBox.ItemsSource = All.Concat(typeCats.Where(x => x.RootID == cat.ID).OrderBy(y => y.ID));
            else
                SubCatBox.ItemsSource = None;
            SubCatBox.SelectedIndex = 0;
        }
        private void OnBrowserTabSelected(object sender, RoutedEventArgs e)
        {
            managerSelected = false;
            if (!selected)
                InitializeBrowser();
        }
        bool managerSelected = true;
        private void OnManagerTabSelected(object sender, RoutedEventArgs e)
        {
            managerSelected = true;
        }

        private static int page = 1;
        private void DecrementPage(object sender, RoutedEventArgs e)
        {
            --page;
            RefreshFilter();
        }
        private void IncrementPage(object sender, RoutedEventArgs e)
        {
            ++page;
            RefreshFilter();
        }
        private void BrowserRefresh(object sender, RoutedEventArgs e)
        {
            if (!selected)
                InitializeBrowser();
            else
                RefreshFilter();
        }
        private static bool filterSelect;
        private static bool searched = false;
        private async void RefreshFilter()
        {
            NSFWCheckbox.IsEnabled = false;
            SearchBar.IsEnabled = false;
            SearchButton.IsEnabled = false;
            FilterBox.IsEnabled = false;
            TypeBox.IsEnabled = false;
            CatBox.IsEnabled = false;
            SubCatBox.IsEnabled = false;
            PageLeft.IsEnabled = false;
            PageRight.IsEnabled = false;
            PageBox.IsEnabled = false;
            PerPageBox.IsEnabled = false;
            ClearCacheButton.IsEnabled = false;
            ErrorPanel.Visibility = Visibility.Collapsed;
            filterSelect = true;
            PageBox.SelectedValue = page;
            filterSelect = false;
            Page.Text = $"Page {page}";
            LoadingBar.Visibility = Visibility.Visible;
            FeedBox.Visibility = Visibility.Collapsed;
            PageLeft.IsEnabled = false;
            PageRight.IsEnabled = false;
            var search = searched ? SearchBar.Text : null;
            await FeedGenerator.GetFeed(page, (TypeFilter)TypeBox.SelectedIndex, (FeedFilter)FilterBox.SelectedIndex, (GameBananaCategory)CatBox.SelectedItem,
                (GameBananaCategory)SubCatBox.SelectedItem, (PerPageBox.SelectedIndex + 1) * 10, (bool)NSFWCheckbox.IsChecked, search);
            FeedBox.ItemsSource = FeedGenerator.CurrentFeed.Records;
            if (FeedGenerator.error)
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility = Visibility.Visible;
                BrowserRefreshButton.Visibility = Visibility.Visible;
                if (FeedGenerator.exception.Message.Contains("JSON tokens"))
                {
                    BrowserMessage.Text = "Uh oh! SUPER SHENRON failed to deserialize the GameBanana feed.";
                    return;
                }
                switch (Regex.Match(FeedGenerator.exception.Message, @"\d+").Value)
                {
                    case "443":
                        BrowserMessage.Text = "Your internet connection is down.";
                        break;
                    case "500":
                    case "503":
                    case "504":
                        BrowserMessage.Text = "GameBanana's servers are down.";
                        break;
                    default:
                        BrowserMessage.Text = FeedGenerator.exception.Message;
                        break;
                }
                return;
            }
            if (page < FeedGenerator.CurrentFeed.TotalPages)
                PageRight.IsEnabled = true;
            if (page != 1)
                PageLeft.IsEnabled = true;
            if (FeedBox.Items.Count > 0)
            {
                FeedBox.ScrollIntoView(FeedBox.Items[0]);
                FeedBox.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorPanel.Visibility = Visibility.Visible;
                BrowserRefreshButton.Visibility = Visibility.Collapsed;
                BrowserMessage.Visibility = Visibility.Visible;
                BrowserMessage.Text = "SUPER SHENRON couldn't find any mods.";
            }
            PageBox.ItemsSource = Enumerable.Range(1, (int)(FeedGenerator.CurrentFeed.TotalPages));

            LoadingBar.Visibility = Visibility.Collapsed;
            CatBox.IsEnabled = true;
            SubCatBox.IsEnabled = true;
            TypeBox.IsEnabled = true;
            FilterBox.IsEnabled = true;
            PageBox.IsEnabled = true;
            PerPageBox.IsEnabled = true;
            SearchBar.IsEnabled = true;
            SearchButton.IsEnabled = true;
            NSFWCheckbox.IsEnabled = true;
            ClearCacheButton.IsEnabled = true;
        }

        private void FilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && !filterSelect)
            {
                if (!searched || FilterBox.SelectedIndex != 3)
                {
                    filterSelect = true;
                    var temp = FilterBox.SelectedIndex;
                    FilterBox.ItemsSource = FilterBoxList;
                    FilterBox.SelectedIndex = temp;
                    filterSelect = false;
                }
                SearchBar.Clear();
                searched = false;
                page = 1;
                RefreshFilter();
            }
        }
        private void PerPageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && !filterSelect)
            {
                page = 1;
                RefreshFilter();
            }
        }
        private void TypeFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && !filterSelect)
            {
                SearchBar.Clear();
                searched = false;
                filterSelect = true;
                if (!searched)
                {
                    FilterBox.ItemsSource = FilterBoxList;
                    FilterBox.SelectedIndex = 1;
                }
                // Set categories
                SetRootCategories();
                filterSelect = false;
                page = 1;
                RefreshFilter();
            }
        }
        private void MainFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && !filterSelect)
            {
                SearchBar.Clear();
                searched = false;
                filterSelect = true;
                if (!searched)
                {
                    FilterBox.ItemsSource = FilterBoxList;
                    FilterBox.SelectedIndex = 1;
                }
                // Set Categories
                SetSubCategories();
                filterSelect = false;
                page = 1;
                RefreshFilter();
            }
        }
        private void SubFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!filterSelect && IsLoaded)
            {
                SearchBar.Clear();
                searched = false;
                page = 1;
                RefreshFilter();
            }
        }
        private void UniformGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var grid = sender as UniformGrid;
            grid.Columns = (int)grid.ActualWidth / 400 + 1;
        }
        private void OnResize(object sender, RoutedEventArgs e)
        {
            BigScreenshot.MaxHeight = ActualHeight - 240;
        }

        private void PageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!filterSelect && IsLoaded)
            {
                page = (int)PageBox.SelectedValue;
                RefreshFilter();
            }
        }
        private void NSFWCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (!filterSelect && IsLoaded)
            {
                if (searched)
                {
                    filterSelect = true;
                    FilterBox.ItemsSource = FilterBoxList;
                    FilterBox.SelectedIndex = 1;
                    filterSelect = false;
                }
                SearchBar.Clear();
                searched = false;
                page = 1;
                RefreshFilter();
            }
        }
        private void ClearCache(object sender, RoutedEventArgs e)
        {
            FeedGenerator.ClearCache();
            RefreshFilter();
        }
        private void LoadoutsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;
            // Change the loadout
            else if (LoadoutsBox.SelectedItem != null)
            {
                Global.config.Configs[Global.config.CurrentGame].CurrentLoadout = LoadoutsBox.SelectedItem.ToString();

                // Create loadout if it doesn't exist
                if (!Global.config.Configs[Global.config.CurrentGame].Loadouts.ContainsKey(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout))
                    Global.config.Configs[Global.config.CurrentGame].Loadouts.Add(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout, new());
                else if (Global.config.Configs[Global.config.CurrentGame].Loadouts[Global.config.Configs[Global.config.CurrentGame].CurrentLoadout] == null)
                    Global.config.Configs[Global.config.CurrentGame].Loadouts[Global.config.Configs[Global.config.CurrentGame].CurrentLoadout] = new();

                Global.ModList = Global.config.Configs[Global.config.CurrentGame].Loadouts[Global.config.Configs[Global.config.CurrentGame].CurrentLoadout];
                Refresh();
                Global.logger.WriteLine($"Loadout changed to {LoadoutsBox.SelectedItem}", LoggerType.Info);
            }
        }
        private void EditLoadouts_Click(object sender, RoutedEventArgs e)
        {
            var choices = new List<Choice>();
            choices.Add(new Choice()
            {
                OptionText = "Add New Loadout - Enabled",
                OptionSubText = "Adds a new loadout starting with all mods enabled in alphanumeric order",
                Index = 0
            });
            choices.Add(new Choice()
            {
                OptionText = "Add New Loadout - Disabled",
                OptionSubText = "Adds a new loadout starting with all mods diabled in alphanumeric order",
                Index = 1
            });
            choices.Add(new Choice()
            {
                OptionText = "Copy Loadout",
                OptionSubText = "Creates a copy of current loadout",
                Index = 2
            });
            choices.Add(new Choice()
            {
                OptionText = $"Rename Current Loadout",
                OptionSubText = $"Changes the name of the current loadout",
                Index = 3
            });
            choices.Add(new Choice()
            {
                OptionText = $"Delete Current Loadout",
                OptionSubText = $"Deletes current loadout and switches to first available one",
                Index = 4
            });
            Dispatcher.Invoke(() =>
            {
                var choice = new ChoiceWindow(choices, $"Loadout Options for {Global.config.CurrentGame}");
                choice.ShowDialog();
                if (choice.choice != null)
                {
                    switch ((int)choice.choice)
                    {
                        // Add new loadout
                        case 0:
                            var newLoadoutWindow = new EditWindow(null, false);
                            newLoadoutWindow.ShowDialog();
                            if (!String.IsNullOrEmpty(newLoadoutWindow.loadout))
                            {
                                Global.LoadoutItems.Add(newLoadoutWindow.loadout);
                                LoadoutsBox.SelectedItem = newLoadoutWindow.loadout;
                            }
                            ShowMetadata(null);
                            break;
                        // Add new Blank loadout
                        case 1:
                            var blankLoadoutWindow = new EditWindow(null, false);
                            blankLoadoutWindow.ShowDialog();
                            if (!String.IsNullOrEmpty(blankLoadoutWindow.loadout))
                            {
                                Global.LoadoutItems.Add(blankLoadoutWindow.loadout);
                                LoadoutsBox.SelectedItem = blankLoadoutWindow.loadout;
                                Global.config.Configs[Global.config.CurrentGame].DisableAllMods(blankLoadoutWindow.loadout);
                            }
                            ShowMetadata(null);
                            break;
                        // Copy current loadout
                        case 2:
                            if (true)
                            {
                                // Insert new name at index of original loadout
                                Global.LoadoutItems.Insert(Global.LoadoutItems.IndexOf(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout) + 1, Global.config.Configs[Global.config.CurrentGame].CurrentLoadout + " - Copy");
                                // Copy over current loadout
                                Global.config.Configs[Global.config.CurrentGame].Loadouts.Add(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout + " - Copy", Global.ModList);
                                // Delete current loadout
                                // Trigger selection changed event
                                LoadoutsBox.SelectedItem = Global.config.Configs[Global.config.CurrentGame].CurrentLoadout + " - Copy";
                            }
                            break;
                        // Rename current loadout
                        case 3:
                            var renameLoadoutWindow = new EditWindow(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout, false);
                            renameLoadoutWindow.ShowDialog();
                            if (!String.IsNullOrEmpty(renameLoadoutWindow.loadout))
                            {
                                // Insert new name at index of original loadout
                                Global.LoadoutItems.Insert(Global.LoadoutItems.IndexOf(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout), renameLoadoutWindow.loadout);
                                // Copy over current loadout
                                Global.config.Configs[Global.config.CurrentGame].Loadouts.Add(renameLoadoutWindow.loadout, Global.ModList);
                                // Delete current loadout
                                Global.LoadoutItems.Remove(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout);
                                Global.config.Configs[Global.config.CurrentGame].Loadouts.Remove(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout);
                                // Trigger selection changed event
                                LoadoutsBox.SelectedItem = renameLoadoutWindow.loadout;
                            }
                            break;
                        // Delete current loadout
                        case 4:
                            if (Global.config.Configs[Global.config.CurrentGame].Loadouts.Count == 1)
                            {
                                Global.logger.WriteLine("Unable to delete current loadout since there is only one", LoggerType.Error);
                                return;
                            }
                            else
                            {
                                Global.LoadoutItems.Remove(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout);
                                Global.config.Configs[Global.config.CurrentGame].Loadouts.Remove(Global.config.Configs[Global.config.CurrentGame].CurrentLoadout);
                                // Triggers selection changed event
                                LoadoutsBox.SelectedIndex = 0;
                            }
                            ShowMetadata(null);
                            break;
                    }
                }
            });
        }

        private void Search()
        {
            if (!filterSelect && IsLoaded && !String.IsNullOrWhiteSpace(SearchBar.Text))
            {
                filterSelect = true;
                FilterBox.ItemsSource = FilterBoxListWhenSearched;
                FilterBox.SelectedIndex = 3;
                NSFWCheckbox.IsChecked = true;
                // Set categories
                SetRootCategories();
                filterSelect = false;
                searched = true;
                page = 1;
                RefreshFilter();
            }
        }
        private void SearchBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Search();
        }
        private static readonly List<string> FilterBoxList = new string[] { "Featured", "Recent", "Popular" }.ToList();
        private static readonly List<string> FilterBoxListWhenSearched = new string[] { "Featured", "Recent", "Popular", "- - -" }.ToList();

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            Search();
        }

        private void ModGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && ModGrid.CurrentColumn.Header.ToString() != "Enabled")
                foreach (var item in ModGrid.SelectedItems)
                {
                    var checkbox = ModGrid.Columns[0].GetCellContent(item) as CheckBox;
                    if (checkbox != null)
                        checkbox.IsChecked = !checkbox.IsChecked;
                }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsLoaded && managerSelected && ModGridSearchButton.IsEnabled)
                if (e.KeyboardDevice.IsKeyDown(Key.LeftCtrl) || e.KeyboardDevice.IsKeyDown(Key.RightCtrl))
                {
                    switch (e.Key)
                    {
                        case Key.F:
                            ModGrid_SearchBar.Focus();
                            break;
                    }
                }
        }

        private void ModGrid_SearchBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyboardDevice.IsKeyDown(Key.Enter))
                ModGridSearch();
        }
        private void ModGridSearch()
        {
            if (!String.IsNullOrEmpty(ModGrid_SearchBar.Text) && ModGridSearchButton.IsEnabled)
            {
                object focusedItem = null;
                ModGrid.SelectedItems.Clear();
                string text = ModGrid_SearchBar.Text;
                for (int i = 0; i < ModGrid.Items.Count; i++)
                {
                    object item = ModGrid.Items[i];
                    ModGrid.ScrollIntoView(item);
                    DataGridRow row = (DataGridRow)ModGrid.ItemContainerGenerator.ContainerFromIndex(i);
                    TextBlock cellContent = ModGrid.Columns[1].GetCellContent(row) as TextBlock;
                    if (cellContent != null && cellContent.Text.Contains(text, StringComparison.InvariantCultureIgnoreCase))
                    {
                        ModGrid.SelectedItems.Add(item);
                        if (ModGrid.SelectedItems.Count == 1)
                            focusedItem = item;
                    }
                }
                if (focusedItem != null)
                    ModGrid.ScrollIntoView(focusedItem);
                else
                {
                    ShowMetadata(null);
                    Global.logger.WriteLine($"No mods found matching {text}", LoggerType.Info);
                }
            }
        }

        private void ModGridSearchButton_Click(object sender, RoutedEventArgs e)
        {
            ModGridSearch();
        }

        private void Clear_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ModGrid_SearchBar.Clear();
        }
        private async void SortAlphabeticallyAndGroupEnabled_Click(object sender, RoutedEventArgs e)
        {
            DataGridColumnHeader colHeader = sender as DataGridColumnHeader;
            if (colHeader != null)
            {
                if (colHeader.Column.Header.Equals("Name"))
                {
                    var choice = MessageBox.Show($"Confirm sorting all mods alphanumerically?", "SUPER SHENRON", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (choice == MessageBoxResult.No)
                        return;
                    // Sort alphanumerically
                    Global.ModList = new ObservableCollection<Mod>(Global.ModList.ToList().OrderBy(x => x.name, new NaturalSort()).ToList());
                    Global.logger.WriteLine("Sorted alphanumerically!", LoggerType.Info);
                }
                else if (colHeader.Column.Header.Equals("Enabled"))
                {
                    var choice = MessageBox.Show($"Confirm moving all enabled mods to the top?", "SUPER SHENRON", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (choice == MessageBoxResult.No)
                        return;
                    // Move all enabled mods to top
                    Global.ModList = new ObservableCollection<Mod>(Global.ModList.ToList().OrderByDescending(x => x.enabled).ToList());
                    Global.logger.WriteLine("Moved all enabled mods to the top!", LoggerType.Info);
                }
                await Task.Run(() =>
                {
                    App.Current.Dispatcher.Invoke((Action)delegate
                    {
                        ModGrid.ItemsSource = Global.ModList;
                    });
                });
                Global.config.Configs[Global.config.CurrentGame].ModList = Global.ModList;
            }
            e.Handled = true;
        }

    }
}
