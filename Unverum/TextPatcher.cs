// Based on Unverum's, plus its pull request #67 (per-language text) by Ydonse
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Unverum
{
    public class TextEntries
    {
        public string Language { get; set; }
        public List<Entry> Entries { get; set; }
    }
    public class Entry
    {
        public string header { get; set; }
        public string text { get; set; }
    }
    public static class TextPatcher
    {
        public static bool ExtractBaseFiles(string pakName, string filter, string extractedPath)
        {
            var outputFolder = $"{Global.assemblyLocation}{Global.s}Resources{Global.s}{Global.config.CurrentGame}";
            var umodel = $"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}umodel{Global.s}umodel_64.exe"; 
            var resourcesPath = $"{outputFolder}{Global.s}{extractedPath}";
            var savedPath = $"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}umodel{Global.s}UModelSaved{Global.s}{extractedPath}";
            Directory.CreateDirectory(outputFolder);
            if (!File.Exists(umodel))
            {
                Global.logger.WriteLine($"Missing dependencies, text patching will not work (Try redownloading)", LoggerType.Error);
                return false;
            }
            var pak = $"{Path.GetDirectoryName(Global.config.Configs[Global.config.CurrentGame].ModsFolder)}{Global.s}{pakName}";;
            if (!File.Exists(pak))
            {
                Global.logger.WriteLine($"Couldn't find {pak} to extract text files from, text patching will not work", LoggerType.Error);
                return false;
            }
            Global.logger.WriteLine($"Checking pak file...", LoggerType.Info);
            var pakLength = new FileInfo(pak).Length;
            // First time
            if (Global.config.Configs[Global.config.CurrentGame].PakLength == null)
            {
                Global.config.Configs[Global.config.CurrentGame].PakLength = pakLength;
                Global.UpdateConfig();
            }
            // File size matches (no need to reextract)
            else if (pakLength.Equals(Global.config.Configs[Global.config.CurrentGame].PakLength)
                && File.Exists(resourcesPath))
            {
                Global.logger.WriteLine($"Base files already extracted, continuing to next step", LoggerType.Info);
                return true;
            }
            // File size mismatch
            else if (!pakLength.Equals(Global.config.Configs[Global.config.CurrentGame].PakLength))
            {
                Global.logger.WriteLine("Game files have been updated, reextracting base files for text patching", LoggerType.Info);
                Global.config.Configs[Global.config.CurrentGame].PakLength = pakLength;
                Global.UpdateConfig();
            }
            // Extract files
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.WorkingDirectory = Path.GetDirectoryName(umodel);
            startInfo.FileName = umodel;
            var aes = "b9uW0RKNY91be8HN3Lemi68j6Xsi2l7fQJYsp5oR4al4C4c9kY5E0l90411l9P3L";
            startInfo.Arguments = $@"-save -aes={aes} -path=""{Path.GetDirectoryName(Global.config.Configs[Global.config.CurrentGame].ModsFolder)}"" {filter}";
            Global.logger.WriteLine($"Extracting base files for patching...", LoggerType.Info);
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            }
            if (File.Exists(savedPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resourcesPath));
                File.Move(savedPath, resourcesPath, true);
                File.Move(Path.ChangeExtension(savedPath, ".uasset"), Path.ChangeExtension(resourcesPath, ".uasset"), true);
                Directory.Delete($"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}umodel{Global.s}UModelSaved", true);
                Global.logger.WriteLine($"Successfully extracted base files for patching", LoggerType.Info);
                return true;
            }
            Global.logger.WriteLine($"Failed to extract base files, patching will not work", LoggerType.Error);
            return false;
        }
        public static string GetLanguage(string language)
        {
            if (String.IsNullOrWhiteSpace(language))
                return "INT";
            var loc = language.Trim().ToUpperInvariant();
            return Regex.IsMatch(loc, @"^[A-Z0-9_-]{2,8}$") ? loc : null;
        }
        public static Dictionary<string, Entry> LoadBase(string loc)
        {
            var filter = $"RED/Content/Localization/{loc}/REDGame";
            var extracted = $"RED{Global.s}Content{Global.s}Localization{Global.s}{loc}{Global.s}REDGame.uexp";
            if (ExtractBaseFiles("pakchunk0-WindowsNoEditor.pak", filter, extracted)
                || (loc != "INT" && ExtractBaseFiles("pakchunk1-WindowsNoEditor.pak", filter, extracted)))
                return GetEntries(loc);
            return null;
        }
        public static Dictionary<string, Entry> GetEntries(string loc = "INT")
        {
            var file = $"{Global.assemblyLocation}{Global.s}Resources{Global.s}{Global.config.CurrentGame}{Global.s}RED{Global.s}Content{Global.s}Localization{Global.s}{loc}{Global.s}REDGame.uexp";
            if (!File.Exists(file) || !File.Exists(Path.ChangeExtension(file, ".uasset")))
                return null;
            var bytes = File.ReadAllBytes(file);
            // Convert entire file to unicode string
            var allText = Encoding.Unicode.GetString(bytes[54..(bytes.Length - 4)]);
            // Split by the delimiter 0x0D0A
            var entries = allText.Split("\r\n");
            var counter = 0;
            var dict = new Dictionary<string, Entry>();
            // Convert array of strings to dictionary
            while (counter < entries.Length - 1)
            {
                // Append duplicate headers
                var index = 2;
                while (dict.ContainsKey(entries[counter]))
                {
                    entries[counter] = $"{entries[counter]} ({index})";
                    index++;
                }
                dict.Add(entries[counter], new Entry { text = entries[counter + 1] });
                counter += 2;
            }
            return dict;
        }
        // Always applied last, AFTER any text.json that may come with mods installed. This is done to consistently
        // update the in-game text to match SHENRON.asi functionality and not cause it to be overwritten
        public static readonly Entry[] ForcedEntries = new[]
        {
            new Entry { header = "KYCF_Play", text = "Save State" },
            new Entry { header = "KYCF_Memory", text = "Load State" },
            new Entry { header = "KYCF_Reset", text = "Record/Play Training Dummy" }
        };
        public static Dictionary<string, Entry> ApplyForcedEntries(Dictionary<string, Entry> dict)
        {
            foreach (var forced in ForcedEntries)
                dict = ReplaceEntry(forced, dict);
            return dict;
        }
        public static Dictionary<string, Entry> ReplaceEntry(Entry entry, Dictionary<string, Entry> dict)
        {
            if (entry == null || entry.header == null || entry.text == null)
                return dict;
            if (dict.ContainsKey(entry.header))
            {
                Global.logger.WriteLine($"Replacing {dict[entry.header].text} with {entry.text} at {entry.header}", LoggerType.Info);
                dict[entry.header].text = entry.text;
            }
            else
            {
                Global.logger.WriteLine($"Appending header: {entry.header} with text: {entry.text}", LoggerType.Info);
                dict.Add(entry.header, new Entry { text = entry.text });
            }
            return dict;
        }
        public static void CleanOutput()
        {
            if (Directory.Exists($"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak{Global.s}RED"))
                Directory.Delete($"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak{Global.s}RED", true);
        }
        public static void WriteToFile(Dictionary<string, Entry> dict, string loc = "INT")
        {
            var inputUexp = $"{Global.assemblyLocation}{Global.s}Resources{Global.s}{Global.config.CurrentGame}{Global.s}RED{Global.s}Content{Global.s}Localization{Global.s}{loc}{Global.s}REDGame.uexp";
            var inputUasset = $"{Global.assemblyLocation}{Global.s}Resources{Global.s}{Global.config.CurrentGame}{Global.s}RED{Global.s}Content{Global.s}Localization{Global.s}{loc}{Global.s}REDGame.uasset";
            var outputUexp = $"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak{Global.s}RED{Global.s}Content{Global.s}Localization{Global.s}{loc}{Global.s}REDGame.uexp";
            var outputUasset = $"{Global.assemblyLocation}{Global.s}Dependencies{Global.s}u4pak{Global.s}RED{Global.s}Content{Global.s}Localization{Global.s}{loc}{Global.s}REDGame.uasset";
            var bytes = File.ReadAllBytes(inputUexp).ToList();
            bytes.RemoveRange(54, bytes.Count - 54);
            // Append dictionary of entries
            foreach (var key in dict.Keys)
            {
                var header = Encoding.Unicode.GetBytes($"{Regex.Replace(key, @" \(\d+\)", String.Empty)}\r\n");
                var text = Encoding.Unicode.GetBytes($"{dict[key].text}\r\n");
                bytes.AddRange(header);
                bytes.AddRange(text);
            }
            // End with footer
            var footer = new byte[] { 0xC1, 0x83, 0x2A, 0x9E };
            bytes.AddRange(footer);
            // Update header offsets
            var size = BitConverter.GetBytes(bytes.Count - 56);
            bytes.RemoveRange(36, 8);
            bytes.InsertRange(36, size.Concat(size));
            Directory.CreateDirectory(Path.GetDirectoryName(outputUexp));
            File.WriteAllBytes(outputUexp, bytes.ToArray());
            // Update offset in uasset
            var asset = File.ReadAllBytes(inputUasset).ToList();
            asset.RemoveRange(521, 4);
            asset.InsertRange(521, BitConverter.GetBytes(bytes.Count - 4));
            Directory.CreateDirectory(Path.GetDirectoryName(outputUasset));
            File.WriteAllBytes(outputUasset, asset.ToArray());
        }
    }
}
