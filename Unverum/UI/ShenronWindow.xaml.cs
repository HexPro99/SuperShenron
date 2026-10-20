using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Unverum
{
    public partial class ShenronWindow : Window
    {
        private string win64;
        private string shenronFolder;
        private string delayPath;
        private DelaySetting fileSetting = new DelaySetting() { Mode = DelayMode.Off };
        private int recommended;
        private bool updating;

        public ShenronWindow()
        {
            InitializeComponent();
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width * 0.9);
            Height = Math.Min(Height, area.Height * 0.85);
            ShenronUpdater.PanelOpen = true;
            win64 = ShenronCore.GetWin64Folder();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LocateFiles();
            RefreshDelay();
            RefreshUtilities();
            await RefreshStatus(true);
        }
        private void Window_Closed(object sender, EventArgs e)
        {
            ShenronUpdater.PanelOpen = false;
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LocateFiles()
        {
            shenronFolder = null;
            delayPath = null;
            if (win64 == null)
                return;
            var installed = ShenronCore.FindInstalled(win64);
            shenronFolder = installed != null ? Path.GetDirectoryName(installed) : Path.Combine(win64, "plugins");
            delayPath = ShenronDelayFile.GetPath(shenronFolder);
        }

        private DelaySetting GetChosen()
        {
            if (ModeManual.IsChecked == true)
                return new DelaySetting() { Mode = DelayMode.Manual, Frames = (int)Math.Round(DelaySlider.Value) };
            if (ModeAuto.IsChecked == true)
                return new DelaySetting() { Mode = DelayMode.Auto };
            return new DelaySetting() { Mode = DelayMode.Off };
        }
        private void ShowSetting(DelaySetting setting)
        {
            updating = true;
            ModeOff.IsChecked = setting.Mode == DelayMode.Off;
            ModeAuto.IsChecked = setting.Mode == DelayMode.Auto;
            ModeManual.IsChecked = setting.Mode == DelayMode.Manual;
            DelaySlider.Value = setting.Mode == DelayMode.Manual ? setting.Frames : ShenronDelayFile.NativeDelay;
            updating = false;
            UpdateDelayControls();
        }
        private void UpdateDelayControls()
        {
            var chosen = GetChosen();
            DelaySlider.IsEnabled = chosen.Mode == DelayMode.Manual && DelayCard.IsEnabled;
            DelayValueText.Text = chosen.Mode == DelayMode.Manual ? (chosen.Frames == 1 ? "1 frame" : $"{chosen.Frames} frames")
                : chosen.Mode == DelayMode.Auto ? "Auto" : "Off";
            ApplyButton.IsEnabled = DelayCard.IsEnabled && !chosen.SameAs(fileSetting);
        }
        private void RefreshDelay()
        {
            string text = null;
            if (delayPath == null)
            {
                DelayCard.IsEnabled = false;
                DelayFileText.Text = "Click Setup in the main window first, so SUPER SHENRON knows where Dragon Ball FighterZ is.";
                ShowSetting(fileSetting);
                RefreshLastMatch(null);
                return;
            }
            DelayCard.IsEnabled = true;
            try
            {
                text = ShenronDelayFile.Read(delayPath);
            }
            catch (Exception e)
            {
                Global.logger.WriteLine($"Couldn't read {ShenronDelayFile.FileName} ({e.Message})", LoggerType.Warning);
            }
            fileSetting = ShenronDelayFile.Parse(text);
            ShowSetting(fileSetting);
            DelayFileText.Text = text == null
                ? $"{ShenronDelayFile.FileName} doesn't exist yet. Shenron creates it the first time the game starts, Apply makes it now."
                : $"{ShenronDelayFile.FileName} currently says {fileSetting}.";
            AppliedText.Text = String.Empty;
            RefreshLastMatch(text);
        }
        private void RefreshLastMatch(string text)
        {
            var last = ShenronDelayFile.ParseLastMatch(text);
            if (last == null)
            {
                recommended = 0;
                LastMatchText.Text = "No online match recorded yet. Shenron notes your ping here after each online match.";
                UseRecommendedButton.IsEnabled = false;
                return;
            }
            recommended = last.Recommended;
            LastMatchText.Text = $"Your ping was {last.PingMs} ms, Shenron recommends {last.Recommended} {(last.Recommended == 1 ? "frame" : "frames")} of delay.";
            UseRecommendedButton.IsEnabled = DelayCard.IsEnabled;
        }
        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (updating || !IsLoaded)
                return;
            AppliedText.Text = String.Empty;
            UpdateDelayControls();
        }
        private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updating || !IsLoaded)
                return;
            AppliedText.Text = String.Empty;
            UpdateDelayControls();
        }
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ApplyDelay(GetChosen());
        }
        private void UseRecommended_Click(object sender, RoutedEventArgs e)
        {
            if (recommended < 1)
                return;
            ShowSetting(new DelaySetting() { Mode = DelayMode.Manual, Frames = recommended });
            ApplyDelay(GetChosen());
        }
        private void ApplyDelay(DelaySetting setting)
        {
            if (delayPath == null)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(delayPath));
                ShenronDelayFile.Write(delayPath, setting);
            }
            catch (Exception ex)
            {
                Global.logger.WriteLine($"Couldn't save {ShenronDelayFile.FileName} ({ex.Message})", LoggerType.Error);
                MessageBox.Show($"Couldn't save the frame delay: {ex.Message}", "Shenron", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            fileSetting = setting;
            Global.logger.WriteLine($"Shenron frame delay set to {setting}", LoggerType.Info);
            AppliedText.Text = "Saved. It applies from your next online match.";
            string text = null;
            try
            {
                text = ShenronDelayFile.Read(delayPath);
            }
            catch
            {
            }
            DelayFileText.Text = $"{ShenronDelayFile.FileName} currently says {fileSetting}.";
            RefreshLastMatch(text);
            UpdateDelayControls();
        }

        private async Task RefreshStatus(bool askGitHub)
        {
            CheckButton.IsEnabled = ReinstallButton.IsEnabled = OpenFolderButton.IsEnabled = win64 != null;
            if (win64 == null)
            {
                InstalledText.Text = "Unknown";
                LatestText.Text = String.Empty;
                StatusNote.Text = "Click Setup in the main window first.";
                return;
            }
            var folder = win64;
            var installed = await Task.Run(() => ShenronCore.GetInstalled(folder));
            InstalledText.Text = installed == null ? "Not installed"
                : installed.Tag != null ? installed.Tag : "Installed (not a release SUPER SHENRON put there)";
            var check = ShenronCore.LastCheck;
            if (check == null && askGitHub)
            {
                LatestText.Text = "Checking...";
                StatusNote.Text = String.Empty;
                try
                {
                    check = await Task.Run(() => ShenronCore.CheckAsync(folder, CancellationToken.None));
                    ShenronCore.LastCheck = check;
                }
                catch (Exception e)
                {
                    LatestText.Text = "Couldn't check";
                    StatusNote.Text = e.Message;
                    return;
                }
            }
            ShowCheck(installed, check);
        }
        private void ShowCheck(ShenronInstalled installed, ShenronCheck check)
        {
            if (check == null)
            {
                LatestText.Text = "Not checked yet";
                StatusNote.Text = String.Empty;
                return;
            }
            if (check.Error != null)
            {
                LatestText.Text = "Couldn't reach GitHub";
                StatusNote.Text = check.Error;
                return;
            }
            if (check.Release == null)
            {
                LatestText.Text = "No release found";
                StatusNote.Text = String.Empty;
                return;
            }
            var when = check.CheckedAt.ToLocalTime();
            LatestText.Text = $"{check.Release.TagName} (checked {when:t})";
            string note;
            if (installed == null)
                note = "Shenron isn't in the game yet. Reinstall puts it there.";
            else if (check.NewerInstalledTag != null)
                note = $"The installed {check.NewerInstalledTag} is newer than the latest release.";
            else if (check.UpdateAvailable)
                note = check.Skipped ? "An update is out, but you skipped it. Check for update asks again."
                    : check.LocalBuildDiffers ? "The installed file isn't the official release. Check for update can put the official one back."
                    : "An update is available. Check for update shows what's new.";
            else
                note = "Shenron is up to date.";
            if (ShenronCore.IsGameRunning())
                note += " Dragon Ball FighterZ is running, so an update takes effect the next time the game starts.";
            StatusNote.Text = note;
        }
        private async void Check_Click(object sender, RoutedEventArgs e)
        {
            CheckButton.IsEnabled = ReinstallButton.IsEnabled = false;
            LatestText.Text = "Checking...";
            try
            {
                await ShenronUpdater.CheckForUpdate(true);
            }
            finally
            {
                LocateFiles();
                RefreshDelay();
                RefreshUtilities();
                await RefreshStatus(true);
            }
        }
        private async void Reinstall_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show("Download Shenron again and replace the file in your game?\n\nYour frame delay settings are kept.",
                "Reinstall Shenron", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
            CheckButton.IsEnabled = ReinstallButton.IsEnabled = false;
            try
            {
                await ShenronUpdater.Reinstall();
            }
            finally
            {
                LocateFiles();
                RefreshDelay();
                RefreshUtilities();
                await RefreshStatus(true);
            }
        }
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (shenronFolder == null)
                return;
            try
            {
                Directory.CreateDirectory(shenronFolder);
                var asi = ShenronCore.FindInstalled(win64);
                if (asi != null)
                    Process.Start("explorer.exe", $"/select,\"{asi}\"");
                else
                    Process.Start("explorer.exe", $"\"{shenronFolder}\"");
            }
            catch (Exception ex)
            {
                Global.logger.WriteLine($"Couldn't open {shenronFolder} ({ex.Message})", LoggerType.Error);
            }
        }

        private string CrashLogPath => shenronFolder == null ? null : Path.Combine(shenronFolder, "kai_crash.log");
        private void RefreshUtilities()
        {
            var path = CrashLogPath;
            var exists = path != null && File.Exists(path);
            CrashLogButton.IsEnabled = exists;
            CrashLogText.Text = path == null ? String.Empty
                : exists ? "  Shenron wrote a report after a crash." : "  No crash reports, nothing has crashed.";
        }
        private void CrashLog_Click(object sender, RoutedEventArgs e)
        {
            var path = CrashLogPath;
            if (path == null || !File.Exists(path))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Global.logger.WriteLine($"Couldn't open {path} ({ex.Message})", LoggerType.Error);
            }
        }
    }
}
