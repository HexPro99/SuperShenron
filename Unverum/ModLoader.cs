// Based on Unverum's, plus its pull request #79 (Binaries/Win64 passthrough) by ENVY
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SearchOption = System.IO.SearchOption;

namespace Unverum
{
    public static class ModLoader
    {
        // Restore all backups created from previous build
        public static bool Restart(string path, string movies, string splash, string sound)
        {
            try
            {
                // Delete everything in mods folder
                Directory.Delete(path, true);
                Directory.CreateDirectory(path);
                RemoveManifestFiles(path);
                // Reset movies and splash folder
                if (!String.IsNullOrEmpty(movies) && Directory.Exists(movies))
                    RestoreDirectory(movies);
                if (!String.IsNullOrEmpty(splash) && Directory.Exists(splash))
                    RestoreDirectory(splash);
                if (!String.IsNullOrEmpty(sound) && Directory.Exists(sound))
                    RestoreDirectory(sound);
                Global.logger.WriteLine("Restored folders", LoggerType.Info);
            }
            catch (Exception e)
            {
                Global.logger.WriteLine(e.Message, LoggerType.Error);
                return false;
            }
            return true;
        }
        private static string BinariesFolder(string path) =>
            $"{Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path)))}{Global.s}Binaries";
        private static string BinariesManifest(string path) => $"{BinariesFolder(path)}{Global.s}SuperShenronInstalledFiles.txt";
        private static readonly List<string> installedFiles = new();
        private static void TrackInstalled(string file)
        {
            if (!installedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                installedFiles.Add(file);
        }
        private static void WriteManifest(string path)
        {
            var manifest = BinariesManifest(path);
            if (installedFiles.Count == 0)
            {
                if (File.Exists(manifest))
                    File.Delete(manifest);
                return;
            }
            File.WriteAllLines(manifest, installedFiles);
        }
        private static void RemoveManifestFiles(string path)
        {
            var manifest = BinariesManifest(path);
            if (!File.Exists(manifest))
                return;
            var root = BinariesFolder(path) + Global.s;
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in File.ReadAllLines(manifest))
            {
                if (String.IsNullOrWhiteSpace(file) || !file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(file))
                    File.Delete(file);
                var dir = Path.GetDirectoryName(file);
                while (!String.IsNullOrEmpty(dir) && dir.Length > root.Length && dirs.Add(dir))
                    dir = Path.GetDirectoryName(dir);
            }
            foreach (var dir in dirs.OrderByDescending(d => d.Length))
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            File.Delete(manifest);
        }
        private const string BinariesFolderName = "Win64";
        private static bool InBinariesFolder(string modPath, string file)
        {
            var rel = Path.GetRelativePath(modPath, Path.GetDirectoryName(file)).Split(Path.DirectorySeparatorChar);
            return rel.Any(part => part.Equals(BinariesFolderName, StringComparison.OrdinalIgnoreCase));
        }
        private static readonly string[] protectedBinaries =
            { "dsound.dll", @"plugins\SHENRON.asi", "RED-Win64-Shipping.exe", "RED-Win64-Shipping-eac-nop-loaded.exe" };
        private static bool IsProtectedBinary(string relative)
        {
            var rel = relative.Replace('/', '\\').TrimStart('\\');
            return rel.Substring(rel.LastIndexOf('\\') + 1).StartsWith("kai_", StringComparison.OrdinalIgnoreCase)
                || protectedBinaries.Contains(rel, StringComparer.OrdinalIgnoreCase);
        }
        private static void CopyBinaries(string path, List<Mod> mods)
        {
            installedFiles.Clear();
            var target = $"{BinariesFolder(path)}{Global.s}{BinariesFolderName}";
            foreach (var mod in mods)
            {
                var modPath = $@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}{Global.s}{mod.name}";
                if (!Directory.Exists(modPath))
                    continue;
                foreach (var binDir in Directory.GetDirectories(modPath, "*", SearchOption.AllDirectories)
                    .Where(d => Path.GetFileName(d).Equals(BinariesFolderName, StringComparison.OrdinalIgnoreCase)
                        && !InBinariesFolder(modPath, Path.Combine(Path.GetDirectoryName(d), "x"))))
                {
                    foreach (var file in Directory.GetFiles(binDir, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(binDir, file);
                        var newPath = $"{target}{Global.s}{relative}";
                        if (IsProtectedBinary(relative))
                        {
                            Global.logger.WriteLine($"{mod.name}: not replacing {relative}, it is part of the Shenron setup", LoggerType.Warning);
                            continue;
                        }
                        if (File.Exists(newPath) && !installedFiles.Contains(newPath, StringComparer.OrdinalIgnoreCase))
                        {
                            Global.logger.WriteLine($"{mod.name}: {relative} already exists in Binaries and wasn't installed by SUPER SHENRON, leaving it", LoggerType.Warning);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                        File.Copy(file, newPath, true);
                        TrackInstalled(newPath);
                    }
                    Global.logger.WriteLine($"Copied Win64 files from {mod.name} to {target}", LoggerType.Info);
                }
            }
            WriteManifest(path);
        }
        private static readonly JsonSerializerOptions TextJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static void RestoreDirectory(string path)
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                if (File.Exists($"{file}.bak"))
                    File.Move($"{file}.bak", file, true);
        }
        private static int CopyFolder(Dictionary<string, bool> paks, string sourcePath, string targetPath, string defaultSig)
        {
            var counter = 0;
            //Copy all the files & Replaces any files with the same name
            foreach (var path in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(path).Equals(".pak", StringComparison.InvariantCultureIgnoreCase)
                    && paks.ContainsKey(path) && paks[path])
                {
                    var newPath = path.Replace(sourcePath, targetPath).Replace(".pak", "_9_P.pak");
                    Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                    File.Copy(path, newPath, true);
                    if (defaultSig != null)
                    {
                        var sig = Path.ChangeExtension(path, ".sig");
                        var newPathSig = Path.ChangeExtension(newPath, ".sig");
                        // Check if mod folder has corresponding .sig
                        if (File.Exists(sig))
                            File.Copy(sig, newPathSig, true);
                        // Otherwise copy over original game's .sig
                        else if (File.Exists(defaultSig))
                            File.Copy(defaultSig, newPathSig, true);
                        else
                        {
                            Global.logger.WriteLine($"Couldn't find .sig file to go with {newPath}", LoggerType.Warning);
                            continue;
                        }
                    }
                    // Copy over utoc/ucas if they exist
                    if (File.Exists(Path.ChangeExtension(path, ".utoc")) && File.Exists(Path.ChangeExtension(path, ".ucas")))
                    {
                        var utoc = Path.ChangeExtension(path, ".utoc");
                        var ucas = Path.ChangeExtension(path, ".ucas");
                        File.Copy(utoc, utoc.Replace(sourcePath, targetPath).Replace(".utoc", "_9_P.utoc"), true);
                        File.Copy(ucas, ucas.Replace(sourcePath, targetPath).Replace(".ucas", "_9_P.ucas"), true);
                    }
                    counter++;
                }
            }

            return counter;
        }

        private static void ReplaceAsset(string file, string path)
        {
            var filesFound = 0;
            foreach (var oldFile in Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                            .Where(a => Path.GetFileName(a).Equals(Path.GetFileName(file),
                            StringComparison.InvariantCultureIgnoreCase)))
            {
                if (!File.Exists($"{oldFile}.bak"))
                    File.Copy(oldFile, $"{oldFile}.bak", true);
                File.Copy(file, oldFile, true);
                Global.logger.WriteLine($"Replaced {oldFile} with {file}", LoggerType.Info);
                filesFound++;
            }
            if (filesFound == 0)
                Global.logger.WriteLine($"Couldn't find {file} within {path}", LoggerType.Warning);
        }
        private static void PakFiles(string path, string output, string sig)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.FileName = $"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak{Global.s}u4pak.exe";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.WorkingDirectory = $"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak";
            startInfo.Arguments = $"pack \"{output}{Global.s}Unverum_9_P.pak\" {path}";
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            }
            var pak = $"{output}{Global.s}Unverum_9_P.pak";
            if (File.Exists(pak))
            {
                if (sig != null)
                {
                    var newSig = Path.ChangeExtension(pak, ".sig");
                    // Copy over original game's .sig
                    if (File.Exists(sig))
                        File.Copy(sig, newSig, true);
                    else
                        Global.logger.WriteLine($"Couldn't find .sig file to go with {pak}", LoggerType.Warning);
                }
            }
            else
                Global.logger.WriteLine($"Failed to create pak!", LoggerType.Error);
        }
        // Copy over mod files in order of ModList
        public static void Build(string path, List<Mod> mods, bool? patched, string movies, string splash, string sound)
        {
            var languages = new Dictionary<string, Dictionary<string, Entry>>();
            var missingLanguages = new HashSet<string>();
            string sig = null;
            var sigs = Directory.GetFiles(Path.GetDirectoryName(path), "*.sig", SearchOption.TopDirectoryOnly);
            if (sigs.Length > 0)
                sig = sigs[0];
            var folderLetter = 'a';
            var tildes = 0;
            CopyBinaries(path, mods);
            foreach (var mod in mods)
            {
                var priorityName = String.Empty;
                foreach (var tilde in Enumerable.Range(0, tildes))
                    priorityName += "~";
                priorityName += folderLetter;
                var folder = $"{path}{Global.s}{priorityName}{Global.s}{mod.name}";
                var modPath = $@"{Global.assemblyLocation}{Global.s}Mods{Global.s}{Global.config.CurrentGame}{Global.s}{mod.name}";
                // Copy over .paks and .sigs to ~mods folder in order
                if (CopyFolder(mod.paks, modPath, folder, sig) > 0)
                {
                    Global.logger.WriteLine($"Copied paks and sigs from {mod.name} over to {folder}", LoggerType.Info);
                    folderLetter++;
                    if (folderLetter == '{')
                    {
                        folderLetter = 'a';
                        tildes++;
                    }
                }
                foreach (var file in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
                {
                    if (InBinariesFolder(modPath, file))
                        continue;
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    switch (ext)
                    {
                        case ".usm":
                        case ".uasset":
                        case ".mp4":
                            if (!String.IsNullOrEmpty(movies) && Directory.Exists(movies))
                                ReplaceAsset(file, movies);
                            break;
                        case ".bmp":
                            if (!String.IsNullOrEmpty(splash) && Directory.Exists(splash))
                                ReplaceAsset(file, splash);
                            break;
                        case ".awb":
                            if (!String.IsNullOrEmpty(sound) && Directory.Exists(sound))
                                ReplaceAsset(file, sound);
                            break;
                        case ".json":
                            // Skip mod details json
                            if (Path.GetFileName(file).Equals("mod.json", StringComparison.InvariantCultureIgnoreCase))
                                break;
                            // Text patching json
                            if (Path.GetFileName(file).Equals("text.json", StringComparison.InvariantCultureIgnoreCase))
                            {
                                var text = File.ReadAllText(file);
                                TextEntries replacements;
                                try
                                {
                                    replacements = JsonSerializer.Deserialize<TextEntries>(text, TextJsonOptions);
                                }
                                catch (Exception e)
                                {
                                    Global.logger.WriteLine(e.Message, LoggerType.Error);
                                    continue;
                                }
                                if (replacements?.Entries == null)
                                    continue;
                                var loc = TextPatcher.GetLanguage(replacements.Language);
                                if (loc == null)
                                {
                                    Global.logger.WriteLine($"{file}: '{replacements.Language}' isn't a valid language, skipping this file", LoggerType.Error);
                                    continue;
                                }
                                if (missingLanguages.Contains(loc))
                                    continue;
                                if (!languages.TryGetValue(loc, out var langEntries))
                                {
                                    langEntries = TextPatcher.LoadBase(loc);
                                    if (langEntries == null)
                                    {
                                        missingLanguages.Add(loc);
                                        continue;
                                    }
                                    languages[loc] = langEntries;
                                }
                                foreach (var replacement in replacements.Entries)
                                    TextPatcher.ReplaceEntry(replacement, langEntries);
                            }
                            break;
                    }
                }
            }
            if (!languages.ContainsKey("INT") && !missingLanguages.Contains("INT"))
            {
                var intEntries = TextPatcher.LoadBase("INT");
                if (intEntries != null)
                    languages["INT"] = intEntries;
                else
                    Global.logger.WriteLine("Couldn't get the game's text, the training labels won't be set", LoggerType.Warning);
            }
            // Create pak if text was patched
            if (languages.Count > 0)
            {
                TextPatcher.CleanOutput();
                foreach (var language in languages)
                {
                    var langEntries = TextPatcher.ApplyForcedEntries(language.Value);
                    // Write uasset/uexp
                    TextPatcher.WriteToFile(langEntries, language.Key);
                }

                var priorityName = String.Empty;
                foreach (var tilde in Enumerable.Range(0, tildes))
                    priorityName += "~";
                priorityName += folderLetter;
                var folder = $"{path}{Global.s}{priorityName}";
                Directory.CreateDirectory(folder);

                PakFiles("RED", folder, sig);
                // Delete loose files
                Directory.Delete($"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak{Global.s}RED", true);
            }

            // Costume Patched placeholder files as lowest priority
            if (patched != null && (bool)patched)
            {
                var baseFolder = $"{path}{Global.s}--Base--";
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Unverum.Resources.Patches.{Global.config.CurrentGame.Replace(" ", "_").Replace("-", "_")}.Placeholder.--PlaceholderCostumes.pak"))
                {
                    Directory.CreateDirectory(baseFolder);
                    using (var stream = new FileStream($"{baseFolder}{Global.s}--PlaceholderCostumes.pak", FileMode.Create, FileAccess.Write))
                    {
                        resource.CopyTo(stream);
                    }
                }
                if (sig != null)
                {
                    var newSig = $"{baseFolder}{Global.s}--PlaceholderCostumes.sig";
                    // Copy over original game's .sig
                    if (File.Exists(sig))
                        File.Copy(sig, newSig, true);
                    else
                        Global.logger.WriteLine($"Couldn't find .sig file to go with {baseFolder}{Global.s}--PlaceholderCostumes.pak", LoggerType.Warning);
                }
                Global.logger.WriteLine($"Copied over base costume patch files", LoggerType.Info);
            }
            Global.logger.WriteLine("Finished building!", LoggerType.Info);
        }
    }
}
