using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Unverum
{
    public static class Setup
    {
        private const string SteamAppId = "678950";
        private const string GameExe = "DBFighterZ.exe";
        private const string SteamFolderName = "DRAGON BALL FighterZ";

        public static string GetMD5Checksum(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
        }
        // Restores the original exe over Unverum's old costume patch builds. Shenron now fetches the latest
        // costume patcher from Death Chaos's GitHub itself.
        public static bool CheckPatch(string exe)
        {
            // Remove old costume patch for DBFZ
            var originalExe = exe.Replace("-eac-nop-loaded", String.Empty);
            if (!File.Exists(exe))
            {
                if (File.Exists(originalExe))
                    File.Copy(originalExe, exe, true);
            }
            else
            {
                var checksum = GetMD5Checksum(exe).ToLowerInvariant();
                // Check if old patch exe is still there
                switch (checksum)
                {
                    // Old patched exes
                    case "2f9c8178fc8a5cdb3ac1ff8fee888f89":
                    case "ae356567060af7ffa7d0d9e293fafbd9":
                    case "8c442c2913b2be5e4a21730c7c15b39f":
                    case "d21dbaa08aba836a799254b4125967a9":
                    case "ca33c47b6df1f561a068c627786226df":
                        File.Copy(originalExe, exe, true);
                        break;
                }
            }
            var PatchPath = $"{Path.GetDirectoryName(Global.config.Configs[Global.Game].Launcher)}";
            var costumePatch = $"{PatchPath}{Global.s}plugins{Global.s}DBFZExtraCostumesPatch.asi";
            if (!File.Exists(costumePatch)
                || GetMD5Checksum(costumePatch).Equals("e99c11be64f7fb81e8b2eebdff72b164", StringComparison.InvariantCultureIgnoreCase)
                || GetMD5Checksum(costumePatch).Equals("960d0f042522485a56665e156c0d9820", StringComparison.InvariantCultureIgnoreCase))
                GetPatchFiles(PatchPath);
            var configToml = $"{PatchPath}{Global.s}plugins{Global.s}config.toml";
            if (!File.Exists(configToml))
                GetPatchFiles(PatchPath, onlyMissing: true);
            ShenronUpdater.EnsureInstalled(PatchPath);
            return true;
        }
        public static void GetPatchFiles(string PatchPath, bool onlyMissing = false)
        {
            foreach (var file in Assembly.GetExecutingAssembly().GetManifestResourceNames()
                    .Where(x => x.Contains("Dragon_Ball_FighterZ.Patch")))
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(file))
                {
                    var split = file.Split('.');
                    var index = split.ToList().FindIndex(x => x == "Patch");
                    var path = $"{PatchPath}{Global.s}{string.Join(Global.s, split[(index + 1)..(split.Length - 1)])}.{split[split.Length - 1]}";
                    if (onlyMissing && File.Exists(path))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                    {
                        resource.CopyTo(stream);
                    }
                }
        }

        private static List<string> GetSteamLibraries()
        {
            var libraries = new List<string>();
            var steamRoots = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = hklm.OpenSubKey(@"SOFTWARE\Valve\Steam") ?? hklm.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (key?.GetValue("InstallPath") is string p && !String.IsNullOrEmpty(p))
                        steamRoots.Add(p);
                }
                catch (Exception) { }
            }
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key?.GetValue("SteamPath") is string p && !String.IsNullOrEmpty(p))
                    steamRoots.Add(p.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception) { }
            steamRoots.Add(@"C:\Program Files (x86)\Steam");
            foreach (var root in steamRoots.Distinct(StringComparer.InvariantCultureIgnoreCase))
            {
                libraries.Add(root);
                try
                {
                    var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch (Exception) { }
            }
            return libraries.Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        public static string FindGameExe()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {SteamAppId}");
                    if (key?.GetValue("InstallLocation") is string location && !String.IsNullOrEmpty(location))
                    {
                        var exe = Path.Combine(location, GameExe);
                        if (File.Exists(exe))
                            return exe;
                    }
                }
                catch (Exception) { }
            }
            foreach (var library in GetSteamLibraries())
            {
                var exe = Path.Combine(library, "steamapps", "common", SteamFolderName, GameExe);
                if (File.Exists(exe))
                    return exe;
            }
            return null;
        }

        public static bool DBFZ()
        {
            var defaultPath = FindGameExe();
            if (defaultPath == null)
            {
                Global.logger.WriteLine($"Couldn't find Dragon Ball FighterZ automatically, select {GameExe} instead", LoggerType.Warning);
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.DefaultExt = ".exe";
                dialog.Filter = $"Executable Files ({GameExe})|{GameExe}";
                dialog.Title = $"Select {GameExe} from your Dragon Ball FighterZ install folder";
                dialog.Multiselect = false;
                dialog.InitialDirectory = Global.assemblyLocation;
                dialog.ShowDialog();
                if (!String.IsNullOrEmpty(dialog.FileName)
                    && Path.GetFileName(dialog.FileName).Equals(GameExe, StringComparison.InvariantCultureIgnoreCase))
                    defaultPath = dialog.FileName;
                else if (!String.IsNullOrEmpty(dialog.FileName))
                {
                    Global.logger.WriteLine($"Invalid .exe chosen", LoggerType.Error);
                    return false;
                }
                else
                    return false;
            }
            var parent = Path.GetDirectoryName(defaultPath);
            var launcher = $"{parent}{Global.s}RED{Global.s}Binaries{Global.s}Win64{Global.s}RED-Win64-Shipping.exe";
            var renamedLauncher = $"{parent}{Global.s}RED{Global.s}Binaries{Global.s}Win64{Global.s}RED-Win64-Shipping-eac-nop-loaded.exe";
            var ModsFolder = $"{parent}{Global.s}RED{Global.s}Content{Global.s}Paks{Global.s}~mods";
            if (File.Exists(launcher))
                File.Copy(launcher, renamedLauncher, true);
            if (!File.Exists(renamedLauncher))
            {
                Global.logger.WriteLine($"Couldn't find {renamedLauncher}, select the correct exe", LoggerType.Error);
                return false;
            }

            Directory.CreateDirectory(ModsFolder);
            Global.config.Configs[Global.Game].ModsFolder = ModsFolder;
            Global.config.Configs[Global.Game].Launcher = renamedLauncher;

            CheckPatch(renamedLauncher);

            Global.UpdateConfig();
            Global.logger.WriteLine($"Setup completed!", LoggerType.Info);
            return true;
        }
    }
}
