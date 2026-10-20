using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Unverum.UI;

namespace Unverum
{
    public static class ShenronUpdater
    {
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
        // Minimum time between checks, so switching back to the window doesn't query GitHub every time
        public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(10);
        private static readonly Uri LogoUri = new Uri("pack://application:,,,/SuperShenron;component/Assets/Shenron.png");

        private static DispatcherTimer timer;
        private static MainWindow window;
        private static DateTime lastCheck = DateTime.MinValue;
        private static bool busy;
        // An update found while the window wasn't in front, shown the next time it's activated
        private static ShenronCheck pending;
        // Updates already prompted for this session, so the same one isn't asked twice
        private static readonly HashSet<string> askedThisSession = new HashSet<string>();
        // Stops a repeating background error from filling the log
        private static string lastLoggedError;
        // True while the Shenron panel is open, so a prompt doesn't appear on top of it
        public static bool PanelOpen { get; set; }

        public static void StartPeriodicChecks(MainWindow main)
        {
            if (App.SecondaryInstance || main == null || timer != null)
                return;
            window = main;
            timer = new DispatcherTimer { Interval = CheckInterval };
            timer.Tick += (sender, e) => RunBackgroundCheck();
            timer.Start();
            main.Activated += (sender, e) => OnActivated();
        }
        public static void StopPeriodicChecks()
        {
            timer?.Stop();
            timer = null;
        }

        private static void OnActivated()
        {
            if (pending != null && !busy && !PanelOpen && window != null && !window.Busy)
                ShowPending();
            else
                RunBackgroundCheck();
        }

        private static async void RunBackgroundCheck()
        {
            if (App.SecondaryInstance || window == null || busy || PanelOpen || window.Busy)
                return;
            if (!ShenronCore.IsDue(DateTime.UtcNow, lastCheck, MinimumGap))
                return;
            await CheckForUpdate(false);
        }

        private static async void ShowPending()
        {
            var check = pending;
            pending = null;
            if (check == null)
                return;
            busy = true;
            try
            {
                if (ShenronCore.ShouldPrompt(check, askedThisSession, false))
                    await Prompt(check);
            }
            catch (Exception e)
            {
                Global.logger.WriteLine($"Couldn't show the Shenron update ({e.Message})", LoggerType.Error);
            }
            finally
            {
                busy = false;
            }
        }

        public static async Task<bool> CheckForUpdate(bool manual = true)
        {
            if (App.SecondaryInstance)
                return false;
            var win64 = ShenronCore.GetWin64Folder();
            if (win64 == null)
                return false;
            while (busy)
            {
                if (!manual)
                    return false;
                await Task.Delay(250);
            }
            busy = true;
            try
            {
                var check = await Task.Run(() => ShenronCore.CheckAsync(win64, CancellationToken.None));
                lastCheck = DateTime.UtcNow;
                ShenronCore.LastCheck = check;
                if (check.Installed == null)
                {
                    if (check.Error != null)
                        Global.logger.WriteLine($"Couldn't look up the latest Shenron release ({check.Error})", LoggerType.Warning);
                    Global.logger.WriteLine("Shenron isn't installed yet, installing it...", LoggerType.Info);
                    await Install(check, true);
                    return false;
                }
                if (check.Error != null)
                {
                    if (manual || check.Error != lastLoggedError)
                        Global.logger.WriteLine($"Couldn't check for Shenron updates ({check.Error})", LoggerType.Warning);
                    lastLoggedError = check.Error;
                    return false;
                }
                lastLoggedError = null;
                if (check.Release == null)
                {
                    if (manual)
                        Global.logger.WriteLine($"Couldn't find a Shenron download in the {ShenronCore.Repository} releases", LoggerType.Warning);
                    return false;
                }
                var tag = check.Release.TagName;
                if (check.NewerInstalledTag != null)
                {
                    if (manual)
                        Global.logger.WriteLine($"Shenron {check.NewerInstalledTag} is installed, newer than the latest release ({tag})", LoggerType.Info);
                    return false;
                }
                if (!check.UpdateAvailable)
                {
                    if (manual)
                        Global.logger.WriteLine($"Shenron {tag} is up to date", LoggerType.Info);
                    return false;
                }
                if (check.Skipped)
                {
                    if (manual)
                        Global.logger.WriteLine($"Shenron {tag} is out (skipped)", LoggerType.Info);
                    return false;
                }
                if (!ShenronCore.ShouldPrompt(check, askedThisSession, manual))
                    return true;
                if (!manual && (window == null || !window.IsActive || window.WindowState == WindowState.Minimized))
                {
                    if (pending == null || ShenronCore.UpdateKey(pending) != ShenronCore.UpdateKey(check))
                        Global.logger.WriteLine($"An update is available for Shenron ({tag}), you'll be asked when SUPER SHENRON is in front", LoggerType.Info);
                    pending = check;
                    return true;
                }
                await Prompt(check);
                return true;
            }
            catch (Exception e)
            {
                Global.logger.WriteLine($"Couldn't check for Shenron updates ({e.Message})", LoggerType.Error);
                return false;
            }
            finally
            {
                busy = false;
            }
        }

        private static async Task Prompt(ShenronCheck check)
        {
            var tag = check.Release.TagName;
            askedThisSession.Add(ShenronCore.UpdateKey(check));
            pending = null;
            Global.logger.WriteLine($"An update is available for Shenron ({tag})!", LoggerType.Info);
            var text = check.LocalBuildDiffers
                ? $"The Shenron in your game isn't the official {tag} file. Put the official one back?"
                : "A new update is available for Shenron";
            ChangelogBox changelogBox = new ChangelogBox(ShenronCore.ToChangelog(check.Release), "Shenron", text, LogoUri, true);
            changelogBox.PreviewImage.MaxHeight = 200;
            if (window != null && window.IsVisible)
                changelogBox.Owner = window;
            changelogBox.Activate();
            changelogBox.ShowDialog();
            if (changelogBox.Skip)
            {
                ShenronCore.SkipUpdate(check.Release, check.Asset);
                Global.logger.WriteLine("Skipped update for Shenron...", LoggerType.Info);
                return;
            }
            if (!changelogBox.YesNo)
            {
                Global.logger.WriteLine("Declined update for Shenron...", LoggerType.Info);
                return;
            }
            await Install(check, false);
        }

        public static void EnsureInstalled(string win64)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await ShenronCore.EnsureInstalledAsync(win64, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Global.logger.WriteLine($"Couldn't install Shenron ({e.Message})", LoggerType.Error);
                }
            });
            if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
                return;
            task.Wait();
        }

        public static async Task<bool> Reinstall()
        {
            var win64 = ShenronCore.GetWin64Folder();
            if (win64 == null)
            {
                Global.logger.WriteLine("Click Setup first so SUPER SHENRON knows where Dragon Ball FighterZ is", LoggerType.Warning);
                return false;
            }
            while (busy)
                await Task.Delay(250);
            busy = true;
            try
            {
                var check = await Task.Run(() => ShenronCore.CheckAsync(win64, CancellationToken.None));
                lastCheck = DateTime.UtcNow;
                if (check.Error != null)
                    Global.logger.WriteLine($"Couldn't look up the latest Shenron release ({check.Error})", LoggerType.Warning);
                check.Downloaded = null;
                return await Install(check, true);
            }
            catch (Exception e)
            {
                Global.logger.WriteLine($"Couldn't reinstall Shenron ({e.Message})", LoggerType.Error);
                return false;
            }
            finally
            {
                busy = false;
            }
        }

        private static async Task<bool> Install(ShenronCheck check, bool firstInstall)
        {
            var cancellationToken = new CancellationTokenSource();
            ProgressBox progressBox = null;
            var closed = false;
            var installed = false;
            if (check.Downloaded == null)
            {
                progressBox = new ProgressBox(cancellationToken);
                progressBox.progressBar.Value = 0;
                progressBox.finished = false;
                progressBox.Title = $"Download Progress";
                progressBox.Closed += (sender, e) => closed = true;
                progressBox.Show();
                progressBox.Activate();
            }
            var progress = new Progress<DownloadProgress>(x => ReportProgress(progressBox, x));
            try
            {
                installed = await Task.Run(() => ShenronCore.InstallAsync(check.Win64Folder, check.Release, check.Asset, check.Downloaded,
                    firstInstall, progress, cancellationToken.Token));
            }
            catch (OperationCanceledException)
            {
                Global.logger.WriteLine(firstInstall ? "Cancelled installing Shenron" : "Cancelled update for Shenron", LoggerType.Info);
            }
            catch (Exception e)
            {
                Global.logger.WriteLine($"Couldn't install Shenron ({e.Message})", LoggerType.Error);
            }
            finally
            {
                if (progressBox != null && !closed)
                {
                    progressBox.finished = true;
                    progressBox.Close();
                }
            }
            if (installed)
            {
                pending = null;
                ShenronCore.LastCheck = null;
                if (ShenronCore.IsGameRunning())
                    Global.logger.WriteLine("Dragon Ball FighterZ is running, the new Shenron loads the next time the game starts", LoggerType.Info);
            }
            return installed;
        }
        private static void ReportProgress(ProgressBox progressBox, DownloadProgress progress)
        {
            if (progressBox == null)
                return;
            if (progress.Percentage == 1)
                progressBox.finished = true;
            progressBox.progressBar.Value = progress.Percentage * 100;
            progressBox.taskBarItem.ProgressValue = progress.Percentage;
            progressBox.progressTitle.Text = $"Downloading {progress.FileName}...";
            progressBox.progressText.Text = $"{Math.Round(progress.Percentage * 100, 2)}% " +
                $"({StringConverters.FormatSize(progress.DownloadedBytes)} of {StringConverters.FormatSize(progress.TotalBytes)})";
        }
    }
}
