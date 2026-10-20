// Unverum-style update flow, pointed at HexPro99's GitHub releases to check for, download and install the latest SHENRON.asi
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace Unverum
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("body")]
        public string Body { get; set; }
        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }
        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; }
    }
    public class GitHubReleaseAsset
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("size")]
        public long Size { get; set; }
        [JsonPropertyName("digest")]
        public string Digest { get; set; }
        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; }
    }
    public class ShenronState
    {
        public string InstalledTag { get; set; }
        public string InstalledSha256 { get; set; }
        public long? InstalledAssetId { get; set; }
        public string SkippedTag { get; set; }
        public long? SkippedAssetId { get; set; }
    }
    public class ShenronCheck
    {
        public string Win64Folder { get; set; }
        public string Installed { get; set; }
        public GitHubRelease Release { get; set; }
        public GitHubReleaseAsset Asset { get; set; }
        public string Error { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool Skipped { get; set; }
        public string NewerInstalledTag { get; set; }
        public byte[] Downloaded { get; set; }
        public bool LocalBuildDiffers { get; set; }
        public DateTime CheckedAt { get; set; }
    }
    public class ShenronInstalled
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public string Tag { get; set; }
    }

    public static class ShenronCore/// The direct pointer to the Github 
    {
        public const string Game = "Dragon Ball FighterZ";
        public const string FileName = "SHENRON.asi";
        public const string Repository = "HexPro99/DBFZKAI";
        public static readonly string ReleasesApiUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=30";
        public static readonly string LatestDownloadUrl = $"https://github.com/{Repository}/releases/latest/download/{FileName}";
        public const string BundledTag = "v1.1.0b";
        private const string BundledResourceSuffix = ".Resources.Shenron.SHENRON.asi";
        private const string AsiLoaderResourceSuffix = ".Resources.Patches.Dragon_Ball_FighterZ.Patch.dsound.dll";
        private const long MaxDownloadBytes = 256L * 1024 * 1024;

        private static readonly Regex CopyName = new Regex(@"^shenron(?![a-z]).*\.asi$", RegexOptions.IgnoreCase);
        private static readonly object installLock = new object();
        private static readonly object stateLock = new object();
        private static HttpClient client = CreateClient();
        private static readonly object cacheLock = new object();
        private static string releasesETag;
        private static List<GitHubRelease> releasesCache;
        public static ShenronCheck LastCheck { get; set; }
        internal static void UseClient(HttpClient testClient)
        {
            client = testClient ?? CreateClient();
            lock (cacheLock)
            {
                releasesETag = null;
                releasesCache = null;
            }
        }

        private static HttpClient CreateClient()
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(60);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SuperShenron");
            return httpClient;
        }

        public static string GetWin64Folder()
        {
            if (Global.config?.Configs == null || !Global.config.Configs.TryGetValue(Game, out var gameConfig) || gameConfig == null)
                return null;
            var launcher = gameConfig.Launcher;
            if (String.IsNullOrEmpty(launcher) || !File.Exists(launcher))
                return null;
            var folder = Path.GetDirectoryName(launcher);
            if (Path.GetFileName(launcher).Equals("DBFighterZ.exe", StringComparison.OrdinalIgnoreCase))
                folder = Path.Combine(folder, "RED", "Binaries", "Win64");
            return Directory.Exists(folder) ? folder : null;
        }
        public static string GetCanonicalPath(string win64)
        {
            return Path.Combine(win64, "plugins", FileName);
        }
        public static List<string> FindCopies(string win64)
        {
            var copies = new List<string>();
            foreach (var folder in new[] { Path.Combine(win64, "plugins"), Path.Combine(win64, "scripts"), win64 })
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var name = Path.GetFileName(file);
                    if (Path.GetExtension(name).Equals(".asi", StringComparison.OrdinalIgnoreCase) && CopyName.IsMatch(name))
                        copies.Add(file);
                }
            }
            return copies;
        }
        public static string FindInstalled(string win64)
        {
            var canonical = GetCanonicalPath(win64);
            if (File.Exists(canonical))
                return canonical;
            return FindCopies(win64).FirstOrDefault();
        }
        public static void DisableDuplicates(string win64)
        {
            var canonical = GetCanonicalPath(win64);
            if (!File.Exists(canonical))
                return;
            foreach (var copy in FindCopies(win64))
            {
                if (copy.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var disabled = GetUnusedPath($"{copy}.disabled");
                    File.Move(copy, disabled);
                    var folder = Path.GetFileName(Path.GetDirectoryName(copy));
                    Global.logger.WriteLine($"Disabled an extra copy of Shenron ({folder}{Global.s}{Path.GetFileName(copy)} is now {Path.GetFileName(disabled)}) so it doesn't load twice", LoggerType.Warning);
                }
                catch (Exception e)
                {
                    Global.logger.WriteLine($"Found an extra copy of Shenron at {copy} but couldn't disable it ({e.Message}). Delete it so Shenron doesn't load twice", LoggerType.Warning);
                }
            }
        }
        public static void CleanupLeftovers(string win64)
        {
            var plugins = Path.Combine(win64, "plugins");
            if (!Directory.Exists(plugins))
                return;
            foreach (var file in Directory.GetFiles(plugins, $"{FileName}.old*").Concat(Directory.GetFiles(plugins, $"{FileName}.download")))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }
        public static void EnsureAsiLoader(string win64)
        {
            var loader = Path.Combine(win64, "dsound.dll");
            if (File.Exists(loader))
                return;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(AsiLoaderResourceSuffix, StringComparison.OrdinalIgnoreCase));
                if (resourceName == null)
                {
                    Global.logger.WriteLine($"The ASI loader (dsound.dll) Shenron needs is missing from {win64}, click Setup to restore it", LoggerType.Warning);
                    return;
                }
                using (var resource = assembly.GetManifestResourceStream(resourceName))
                using (var stream = new FileStream(loader, FileMode.Create, FileAccess.Write))
                {
                    resource.CopyTo(stream);
                }
                Global.logger.WriteLine($"Restored the ASI loader (dsound.dll) that loads Shenron", LoggerType.Info);
            }
            catch (Exception e)
            {
                Global.logger.WriteLine($"Couldn't restore the ASI loader (dsound.dll) Shenron needs ({e.Message})", LoggerType.Warning);
            }
        }

        public static ShenronState LoadState()
        {
            string error = null;
            lock (stateLock)
            {
                var path = GetStatePath();
                try
                {
                    if (File.Exists(path))
                        return JsonSerializer.Deserialize<ShenronState>(File.ReadAllText(path)) ?? new ShenronState();
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            }
            if (error != null)
                Global.logger.WriteLine($"Couldn't read Shenron.json ({error})", LoggerType.Warning);
            return new ShenronState();
        }
        public static void SaveState(ShenronState state)
        {
            string error = null;
            lock (stateLock)
            {
                try
                {
                    File.WriteAllText(GetStatePath(), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            }
            if (error != null)
                Global.logger.WriteLine($"Couldn't write Shenron.json ({error})", LoggerType.Error);
        }
        private static string GetStatePath()
        {
            return $"{Global.assemblyLocation}{Global.s}Shenron.json";
        }

        public static async Task<List<GitHubRelease>> FetchReleasesAsync(CancellationToken cancellationToken)
        {
            string etag;
            List<GitHubRelease> cached;
            lock (cacheLock)
            {
                etag = releasesETag;
                cached = releasesCache;
            }
            using (var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (etag != null && cached != null)
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                using (var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached != null)
                        return new List<GitHubRelease>(cached);
                    if (!response.IsSuccessStatusCode)
                    {
                        var code = (int)response.StatusCode;
                        var limited = code == 429 || (code == 403 && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0");
                        throw new HttpRequestException(limited ? "GitHub's hourly limit for update checks was hit, try again later" : $"GitHub answered {code} {response.ReasonPhrase}");
                    }
                    var releases = ParseReleases(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var newETag = response.Headers.ETag?.ToString();
                    lock (cacheLock)
                    {
                        releasesETag = newETag;
                        releasesCache = newETag != null ? new List<GitHubRelease>(releases) : null;
                    }
                    return releases;
                }
            }
        }
        public static List<GitHubRelease> ParseReleases(string json)
        {
            return JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? new List<GitHubRelease>();
        }
        public static GitHubReleaseAsset PickAsset(GitHubRelease release)
        {
            if (release?.Assets == null)
                return null;
            var assets = release.Assets.Where(x => !String.IsNullOrEmpty(x.Name) && !String.IsNullOrEmpty(x.DownloadUrl)).ToList();
            return assets.FirstOrDefault(x => x.Name.Equals(FileName, StringComparison.Ordinal))
                ?? assets.FirstOrDefault(x => x.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase))
                ?? assets.FirstOrDefault(x => Path.GetExtension(x.Name).Equals(".asi", StringComparison.OrdinalIgnoreCase) && CopyName.IsMatch(x.Name))
                ?? assets.FirstOrDefault(x => IsArchive(x.Name) && x.Name.Contains("shenron", StringComparison.OrdinalIgnoreCase));
        }
        public static (GitHubRelease release, GitHubReleaseAsset asset) SelectLatest(IEnumerable<GitHubRelease> releases)
        {
            foreach (var release in releases.Where(x => x != null && !x.Draft && !x.Prerelease)
                .OrderByDescending(x => x.PublishedAt ?? DateTime.MinValue))
            {
                var asset = PickAsset(release);
                if (asset != null)
                    return (release, asset);
            }
            return (null, null);
        }

        public static async Task<ShenronCheck> CheckAsync(string win64, CancellationToken cancellationToken)
        {
            List<GitHubRelease> releases;
            try
            {
                releases = await FetchReleasesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                return await CompareAsync(win64, null, e.Message, cancellationToken).ConfigureAwait(false);
            }
            return await CompareAsync(win64, releases, null, cancellationToken).ConfigureAwait(false);
        }
        public static async Task<ShenronCheck> CompareAsync(string win64, List<GitHubRelease> releases, string error, CancellationToken cancellationToken)
        {
            var check = new ShenronCheck() { Win64Folder = win64, Error = error, CheckedAt = DateTime.UtcNow };
            CleanupLeftovers(win64);
            EnsureAsiLoader(win64);
            DisableDuplicates(win64);
            check.Installed = FindInstalled(win64);
            if (releases == null)
                return check;
            (check.Release, check.Asset) = SelectLatest(releases);
            if (check.Asset == null || check.Installed == null)
                return check;

            var state = LoadState();
            var localHash = HashFile(check.Installed);
            bool? same = null;
            var digest = IsArchive(check.Asset.Name) ? null : GetDigest(check.Asset);
            if (digest != null)
                same = digest == localHash;
            else if (state.InstalledSha256 == localHash && state.InstalledAssetId != null)
                same = state.InstalledAssetId == check.Asset.Id;
            if (same == null)
            {
                check.Downloaded = await DownloadAsiAsync(check.Asset, null, cancellationToken).ConfigureAwait(false);
                same = check.Downloaded != null && HashBytes(check.Downloaded) == localHash;
            }
            if (same == true)
            {
                if (state.InstalledTag != check.Release.TagName || state.InstalledSha256 != localHash || state.InstalledAssetId != check.Asset.Id)
                {
                    state.InstalledTag = check.Release.TagName;
                    state.InstalledSha256 = localHash;
                    state.InstalledAssetId = check.Asset.Id;
                    SaveState(state);
                }
                return check;
            }
            var installedRelease = FindReleaseOf(releases, localHash, state);
            if (installedRelease != null && (installedRelease.PublishedAt ?? DateTime.MinValue) > (check.Release.PublishedAt ?? DateTime.MinValue))
            {
                check.NewerInstalledTag = installedRelease.TagName;
                return check;
            }
            check.UpdateAvailable = true;
            check.LocalBuildDiffers = state.InstalledTag != null && state.InstalledTag == check.Release.TagName;
            check.Skipped = state.SkippedAssetId != null && state.SkippedAssetId == check.Asset.Id;
            return check;
        }
        private static GitHubRelease FindReleaseOf(List<GitHubRelease> releases, string localHash, ShenronState state)
        {
            var listed = releases.Where(x => x != null && !x.Draft).ToList();
            var byDigest = listed.FirstOrDefault(x =>
            {
                var asset = PickAsset(x);
                return asset != null && !IsArchive(asset.Name) && GetDigest(asset) == localHash;
            });
            if (byDigest != null)
                return byDigest;
            if (state.InstalledSha256 == localHash && state.InstalledTag != null)
                return listed.FirstOrDefault(x => x.TagName == state.InstalledTag);
            return null;
        }
        public static string UpdateKey(ShenronCheck check)
        {
            return $"{check?.Release?.TagName}|{check?.Asset?.Id}";
        }
        public static bool ShouldPrompt(ShenronCheck check, ISet<string> askedThisSession, bool manual)
        {
            if (check == null || !check.UpdateAvailable || check.Skipped)
                return false;
            return manual || askedThisSession == null || !askedThisSession.Contains(UpdateKey(check));
        }
        public static bool IsDue(DateTime now, DateTime lastCheck, TimeSpan minGap)
        {
            return now < lastCheck || now - lastCheck >= minGap;
        }
        public static ShenronInstalled GetInstalled(string win64)
        {
            var path = FindInstalled(win64);
            if (path == null)
                return null;
            var info = new ShenronInstalled() { Path = path };
            try
            {
                info.Sha256 = HashFile(path);
            }
            catch
            {
                return info;
            }
            var state = LoadState();
            if (state.InstalledSha256 == info.Sha256)
                info.Tag = state.InstalledTag;
            return info;
        }
        public static bool IsGameRunning()
        {
            try
            {
                foreach (var name in new[] { "RED-Win64-Shipping-eac-nop-loaded", "RED-Win64-Shipping", "DBFighterZ" })
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName(name);
                    var running = processes.Length > 0;
                    foreach (var process in processes)
                        process.Dispose();
                    if (running)
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }
        public static void SkipUpdate(GitHubRelease release, GitHubReleaseAsset asset)
        {
            var state = LoadState();
            state.SkippedTag = release?.TagName;
            state.SkippedAssetId = asset?.Id;
            SaveState(state);
        }

        public static async Task<bool> InstallAsync(string win64, GitHubRelease release, GitHubReleaseAsset asset, byte[] data,
            bool firstInstall, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            if (asset != null)
            {
                try
                {
                    if (data == null)
                        data = await DownloadAsiAsync(asset, progress, cancellationToken).ConfigureAwait(false);
                    var problem = Verify(data, asset);
                    if (problem == null)
                        return Install(win64, data, release?.TagName, asset.Id);
                    Global.logger.WriteLine($"Couldn't use Shenron {release?.TagName} from GitHub ({problem})", LoggerType.Error);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Global.logger.WriteLine($"Couldn't download Shenron {release?.TagName} ({e.Message})", LoggerType.Error);
                }
                if (!firstInstall)
                    return false;
            }
            try
            {
                var latest = await DownloadBytesAsync(LatestDownloadUrl, FileName, progress, cancellationToken).ConfigureAwait(false);
                var problem = Verify(latest, null);
                if (problem == null)
                    return Install(win64, latest, null, null);
                Global.logger.WriteLine($"Couldn't use the latest Shenron from GitHub ({problem})", LoggerType.Warning);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                Global.logger.WriteLine($"Couldn't download the latest Shenron from GitHub ({e.Message})", LoggerType.Warning);
            }
            var bundled = ReadBundled();
            if (bundled != null && Verify(bundled, null) == null)
            {
                Global.logger.WriteLine($"Installing the copy of Shenron that comes with SUPER SHENRON ({BundledTag}), it updates once GitHub can be reached", LoggerType.Warning);
                return Install(win64, bundled, BundledTag, null);
            }
            Global.logger.WriteLine($"Couldn't install Shenron, get {FileName} from github.com/{Repository}/releases and put it in {Path.Combine(win64, "plugins")}", LoggerType.Error);
            return false;
        }
        public static async Task EnsureInstalledAsync(string win64, CancellationToken cancellationToken)
        {
            if (String.IsNullOrEmpty(win64) || !Directory.Exists(win64))
                return;
            CleanupLeftovers(win64);
            EnsureAsiLoader(win64);
            if (FindInstalled(win64) != null)
            {
                DisableDuplicates(win64);
                return;
            }
            Global.logger.WriteLine("Shenron isn't installed yet, installing it...", LoggerType.Info);
            GitHubRelease release = null;
            GitHubReleaseAsset asset = null;
            try
            {
                (release, asset) = SelectLatest(await FetchReleasesAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                Global.logger.WriteLine($"Couldn't look up the latest Shenron release ({e.Message})", LoggerType.Warning);
            }
            await InstallAsync(win64, release, asset, null, true, null, cancellationToken).ConfigureAwait(false);
        }

        private static bool Install(string win64, byte[] data, string tag, long? assetId)
        {
            var plugins = Path.Combine(win64, "plugins");
            var target = GetCanonicalPath(win64);
            var temp = $"{target}.download";
            var inUse = false;
            string error = null;
            lock (installLock)
            {
                try
                {
                    Directory.CreateDirectory(plugins);
                    File.WriteAllBytes(temp, data);
                    try
                    {
                        File.Move(temp, target, true);
                    }
                    catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && File.Exists(target))
                    {
                        File.Move(target, GetUnusedPath($"{target}.old"));
                        File.Move(temp, target);
                        inUse = true;
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch
                    {
                    }
                }
            }
            if (error != null)
            {
                Global.logger.WriteLine($"Couldn't write {target} ({error})", LoggerType.Error);
                return false;
            }
            var state = LoadState();
            state.InstalledTag = tag;
            state.InstalledSha256 = HashBytes(data);
            state.InstalledAssetId = assetId;
            SaveState(state);
            Global.logger.WriteLine($"Installed Shenron{(tag != null ? $" {tag}" : String.Empty)} to {plugins}", LoggerType.Info);
            if (inUse)
                Global.logger.WriteLine("SHENRON.asi was in use (is DBFZ running?), so the new Shenron loads the next time the game starts", LoggerType.Warning);
            EnsureAsiLoader(win64);
            DisableDuplicates(win64);
            return true;
        }
        private static string Verify(byte[] data, GitHubReleaseAsset asset)
        {
            if (data == null)
                return asset != null && IsArchive(asset.Name) ? $"no Shenron .asi inside {asset.Name}" : "nothing was downloaded";
            var digest = asset != null && !IsArchive(asset.Name) ? GetDigest(asset) : null;
            if (digest != null && digest != HashBytes(data))
                return "the download doesn't match GitHub's checksum";
            if (!IsAsi(data))
                return "the file isn't a 64-bit .asi";
            return null;
        }
        public static bool IsAsi(byte[] data)
        {
            if (data == null || data.Length < 0x200 || data[0] != 'M' || data[1] != 'Z')
                return false;
            var pe = BitConverter.ToInt32(data, 0x3C);
            if (pe <= 0 || pe > data.Length - 6)
                return false;
            if (data[pe] != 'P' || data[pe + 1] != 'E' || data[pe + 2] != 0 || data[pe + 3] != 0)
                return false;
            return BitConverter.ToUInt16(data, pe + 4) == 0x8664;
        }

        public static async Task<byte[]> DownloadAsiAsync(GitHubReleaseAsset asset, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            var data = await DownloadBytesAsync(asset.DownloadUrl, asset.Name, progress, cancellationToken).ConfigureAwait(false);
            return IsArchive(asset.Name) ? ExtractAsi(data) : data;
        }
        public static async Task<byte[]> DownloadBytesAsync(string url, string fileName, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
                var total = response.Content.Headers.ContentLength;
                if (total > MaxDownloadBytes)
                    throw new HttpRequestException($"{fileName} is too big ({StringConverters.FormatSize((long)total)})");
                using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await source.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        received += read;
                        if (received > MaxDownloadBytes)
                            throw new HttpRequestException($"{fileName} is too big");
                        if (progress != null && total > 0)
                            progress.Report(new DownloadProgress((float)received / (long)total, received, (long)total, fileName));
                    }
                    progress?.Report(new DownloadProgress(1, received, total ?? received, fileName));
                    return buffer.ToArray();
                }
            }
        }
        public static byte[] ExtractAsi(byte[] archiveData)
        {
            if (archiveData == null)
                return null;
            using (var stream = new MemoryStream(archiveData))
            using (var archive = ArchiveFactory.Open(stream))
            {
                var files = archive.Entries.Where(x => !x.IsDirectory && x.Key != null).ToList();
                var entry = files.FirstOrDefault(x => GetEntryName(x.Key).Equals(FileName, StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(x => CopyName.IsMatch(GetEntryName(x.Key)));
                if (entry == null)
                    return null;
                using (var entryStream = entry.OpenEntryStream())
                using (var output = new MemoryStream())
                {
                    entryStream.CopyTo(output);
                    return output.ToArray();
                }
            }
        }
        private static string GetEntryName(string key)
        {
            var slash = key.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 ? key.Substring(slash + 1) : key;
        }
        public static bool IsArchive(string name)
        {
            var extension = Path.GetExtension(name ?? String.Empty);
            return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase);
        }
        public static byte[] ReadBundled()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(BundledResourceSuffix, StringComparison.OrdinalIgnoreCase));
                if (resourceName == null)
                    return null;
                using (var resource = assembly.GetManifestResourceStream(resourceName))
                using (var output = new MemoryStream())
                {
                    resource.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        public static string GetDigest(GitHubReleaseAsset asset)
        {
            var digest = asset?.Digest;
            if (String.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return null;
            return digest.Substring("sha256:".Length).Trim().ToLowerInvariant();
        }
        public static string HashBytes(byte[] data)
        {
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }
        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        private static string GetUnusedPath(string path)
        {
            if (!File.Exists(path))
                return path;
            for (var i = 2; ; i++)
                if (!File.Exists($"{path}{i}"))
                    return $"{path}{i}";
        }

        public static GameBananaItemUpdate ToChangelog(GitHubRelease release)
        {
            var notes = new StringBuilder();
            var body = (release?.Body ?? String.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.Trim();
                if (Regex.IsMatch(line, @"^([-*_=])\1{2,}$"))
                    continue;
                var item = Regex.Match(line, @"^(?:[-*+\u2022]|\d+[.)])\s+(.+)$");
                if (item.Success)
                {
                    var text = CleanMarkdown(item.Groups[1].Value);
                    if (text.Length > 0)
                    {
                        var indent = Math.Min(3, (rawLine.Length - rawLine.TrimStart().Length) / 2);
                        notes.Append(new string(' ', indent * 2)).Append('\u2022').Append(' ').Append(text).Append('\n');
                    }
                }
                else
                {
                    var text = CleanMarkdown(line);
                    if (text.Length > 0 || notes.Length > 0)
                        notes.Append(text).Append('\n');
                }
            }
            var name = String.IsNullOrWhiteSpace(release?.Name) ? "Shenron" : release.Name.Trim();
            var tag = release?.TagName ?? String.Empty;
            var noteText = Regex.Replace(notes.ToString().Trim(), @"\n{3,}", "\n\n");
            if (noteText.Length == 0 && release?.HtmlUrl != null)
                noteText = $"Release notes: {release.HtmlUrl}";
            var core = tag.TrimStart('v', 'V');
            var nameHasVersion = tag.Length == 0 || name.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0
                || (core.Length > 0 && name.IndexOf(core, StringComparison.OrdinalIgnoreCase) >= 0);
            return new GameBananaItemUpdate()
            {
                Title = name,
                Version = nameHasVersion ? String.Empty : tag,
                Changes = new GameBananaItemUpdateChange[0],
                Text = noteText,
                DateAddedLong = release?.PublishedAt != null ? new DateTimeOffset(release.PublishedAt.Value.ToUniversalTime()).ToUnixTimeSeconds() : 0
            };
        }
        public static string CleanMarkdown(string text)
        {
            text = Regex.Replace(text, @"<[^>]*>", String.Empty);
            text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", String.Empty);
            text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"^#{1,6}\s*", String.Empty);
            text = Regex.Replace(text, @"^>\s?", String.Empty);
            text = Regex.Replace(text, @"^\[[ xX]\]\s*", String.Empty);
            text = Regex.Replace(text, @"\\([\\`*_{}\[\]()#+\-.!~|<>])", m => ((char)(0xE000 + m.Groups[1].Value[0])).ToString());
            text = Regex.Replace(text, @"`+([^`]*)`+", "$1");
            text = Regex.Replace(text, @"(?<![\w*])\*\*(?=\S)(.+?)(?<=\S)\*\*(?![\w*])", "$1");
            text = Regex.Replace(text, @"(?<![\w_])__(?=\S)(.+?)(?<=\S)__(?![\w_])", "$1");
            text = Regex.Replace(text, @"(?<![\w*])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\w*])", "$1");
            text = Regex.Replace(text, @"(?<![\w_])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![\w_])", "$1");
            text = Regex.Replace(text, @"~~(?=\S)(.+?)(?<=\S)~~", "$1");
            text = Regex.Replace(text, "[\uE000-\uE0FF]", m => ((char)(m.Value[0] - 0xE000)).ToString());
            return text.Trim();
        }
        private static string GetCategory(string text)// Unverum's GameBanana changelog categories, picked from the first word of a GitHub release note line
        {
            switch (Regex.Match(text, @"^[A-Za-z]+").Value.ToLowerInvariant())
            {
                case "fix":
                case "fixed":
                case "fixes":
                case "bugfix":
                case "bug":
                case "bugs":
                case "patched":
                    return "BugFix";
                case "add":
                case "added":
                case "adds":
                case "new":
                case "introduced":
                    return "Addition";
                case "improve":
                case "improved":
                case "improves":
                case "better":
                case "enhanced":
                    return "Improvement";
                case "optimize":
                case "optimized":
                case "optimise":
                case "optimised":
                case "faster":
                    return "Optimization";
                case "remove":
                case "removed":
                case "removes":
                case "deleted":
                case "dropped":
                    return "Removal";
                case "tweak":
                case "tweaked":
                case "tweaks":
                    return "Tweak";
                case "adjust":
                case "adjusted":
                case "change":
                case "changed":
                case "changes":
                case "update":
                case "updated":
                case "moved":
                case "renamed":
                    return "Adjustment";
                case "overhaul":
                case "overhauled":
                case "rework":
                case "reworked":
                case "rewrote":
                    return "Overhaul";
                case "refactor":
                case "refactored":
                    return "Refactor";
                default:
                    return "Feature";
            }
        }
    }
}
