///Reads/Edits the kai_delay.txt file to allow in-launcher delay settings. Can be editing directly from the text file if need be
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Unverum
{
    public enum DelayMode
    {
        Off,
        Auto,
        Manual
    }
    public class DelaySetting
    {
        public DelayMode Mode { get; set; }
        public int Frames { get; set; }
        public override string ToString()///mode select: Manual, Auto, and Off
        {
            return Mode == DelayMode.Manual ? $"delay={Frames}" : Mode == DelayMode.Auto ? "delay=auto" : "delay=off";
        }
        public bool SameAs(DelaySetting other)
        {
            return other != null && Mode == other.Mode && (Mode != DelayMode.Manual || Frames == other.Frames);
        }
    }
    public class LastMatch
    {
        public int PingMs { get; set; }/// Grabbed from SHENRON.asi
        public int Recommended { get; set; }/// Equated below
    }

    public static class ShenronDelayFile/// Creates kai_delay.txt if not there
    {
        public const string FileName = "kai_delay.txt";
        public const int MaxDelay = 10;
        public const int NativeDelay = 2;
        public const string DefaultText =
            "# Shenron online input delay (frames). Local only: your opponent does not need the mod or the same number.\r\n" +
            "# 1 frame = 16.7 ms. Lower = more responsive but more rollbacks; higher = fewer rollbacks but more input lag.\r\n" +
            "# Edit the LAST line: off = the game's own choice, auto = Shenron picks from your ping, or 0 to 10 frames.\r\n" +
            "# Lines starting with # are ignored. A change made during a match applies from the next match.\r\n" +
            "# During each match Shenron writes the ping and a recommended delay below (one-way ping in frames).\r\n" +
            "delay=off\r\n";
        private static readonly Encoding FileEncoding = Encoding.Latin1;
        private static readonly Regex SettingLine = new Regex(@"^[ \t]*delay[ \t]*=", RegexOptions.IgnoreCase);
        private static readonly Regex LastMatchLine = new Regex(@"^# last match: ping (\d+) ms -> recommended delay=(\d+)", RegexOptions.Multiline);

        public static int Recommend(int pingMs)
        {
            var r = (int)(((long)pingMs * 3 + 50) / 100);// Half the round-trip ping in 60 fps frames (16.67 ms each), rounded to the nearest frame
            if (r < 1) r = 1;/// Always recommends at least a frame of delay
            if (r > MaxDelay) r = MaxDelay;
            return r;
        }

        public static DelaySetting Parse(string text)/// Delay slider
        {
            var setting = new DelaySetting() { Mode = DelayMode.Off };
            if (text == null)
                return setting;
            foreach (var rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.TrimStart(' ', '\t');
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;
                if (!line.StartsWith("delay", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = line.Substring(5).TrimStart(' ', '\t');
                if (value.Length == 0 || value[0] != '=')
                    continue;
                value = value.Substring(1).TrimStart(' ', '\t');
                if (value.Length > 0 && value[0] >= '0' && value[0] <= '9')
                {
                    var digits = 0;
                    while (digits < value.Length && value[digits] >= '0' && value[digits] <= '9')
                        digits++;
                    var number = value.Substring(0, digits).TrimStart('0');
                    var frames = number.Length == 0 ? 0 : (number.Length > 3 ? MaxDelay : Math.Min(int.Parse(number), MaxDelay));
                    setting = new DelaySetting() { Mode = DelayMode.Manual, Frames = frames };
                }
                else if (value.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
                    setting = new DelaySetting() { Mode = DelayMode.Auto };
                else
                    setting = new DelaySetting() { Mode = DelayMode.Off };
            }
            return setting;
        }

        public static LastMatch ParseLastMatch(string text)///Ping catch from kai_delay.txt, receieves information from SHENRON.asi
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var matches = LastMatchLine.Matches(text);
            if (matches.Count == 0)
                return null;
            var m = matches[matches.Count - 1];
            if (!int.TryParse(m.Groups[1].Value, out var ping) || !int.TryParse(m.Groups[2].Value, out var recommended))
                return null;
            return new LastMatch() { PingMs = ping, Recommended = Math.Max(1, Math.Min(MaxDelay, recommended)) };
        }

        public static string Apply(string text, DelaySetting setting)
        {
            var value = setting.ToString();
            if (string.IsNullOrEmpty(text))
                return DefaultText.Substring(0, DefaultText.Length - "delay=off\r\n".Length) + value + "\r\n";
            var newline = text.Contains("\r\n") ? "\r\n" : (text.Contains('\n') ? "\n" : "\r\n");
            var lines = Regex.Split(text, @"(?<=\n)");
            var last = -1;
            for (var i = 0; i < lines.Length; i++)
                if (SettingLine.IsMatch(lines[i]))
                    last = i;
            if (last >= 0)
            {
                var line = lines[last];
                var ending = line.EndsWith("\r\n") ? "\r\n" : (line.EndsWith("\n") ? "\n" : String.Empty);
                lines[last] = value + ending;
                return string.Concat(lines);
            }
            if (!text.EndsWith("\n") && !text.EndsWith("\r"))
                text += newline;
            else if (text.EndsWith("\r") && !text.EndsWith("\r\n"))
                text += "\n";
            return text + value + newline;
        }

        public static string GetPath(string shenronFolder)
        {
            return Path.Combine(shenronFolder, FileName);
        }
        public static string Read(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, FileEncoding) : null;
        }
        public static void Write(string path, DelaySetting setting)
        {
            var updated = Apply(Read(path), setting);
            var temp = path + ".tmp";
            File.WriteAllText(temp, updated, FileEncoding);
            Exception last = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Move(temp, path, true);
                    return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    last = e;
                    Thread.Sleep(60);
                }
            }
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
            }
            throw last ?? new IOException($"Couldn't write {path}");
        }
    }
}
