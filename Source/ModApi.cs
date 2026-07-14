using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace SrslyModsManager
{
    public sealed class ModApi : IModApi
    {
        public void InitMod(global::Mod modInstance)
        {
            ModFolders.Initialize(modInstance);
            Log.Out("[ModsManager] Ready.");
        }
    }

    internal static class ModFolders
    {
        internal const string WindowGroup = "modManager";
        internal const string DisabledFolderName = "Mods_Disabled";
        private static string managerFolderName = "0_ModsManager";
        private static string managerPath;
        private static string modsPath;
        private static string disabledPath;

        internal static void Initialize(global::Mod modInstance)
        {
            string path = modInstance?.Path;
            if (!string.IsNullOrEmpty(path))
            {
                managerPath = path;
                managerFolderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                modsPath = Directory.GetParent(path)?.FullName;
            }

            if (string.IsNullOrEmpty(modsPath))
            {
                modsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            }

            disabledPath = Path.Combine(Directory.GetParent(modsPath)?.FullName ?? AppDomain.CurrentDomain.BaseDirectory, DisabledFolderName);
        }

        internal static string ResolveAssetPath(ManagedMod mod, string assetPath, string fallbackRelativePath)
        {
            return ExternalTexturePath(ResolveAssetFilePath(mod, assetPath, fallbackRelativePath));
        }

        internal static string ResolveAssetFilePath(ManagedMod mod, string assetPath, string fallbackRelativePath)
        {
            string fallback = ResolveManagerAsset(fallbackRelativePath);
            if (mod == null || string.IsNullOrWhiteSpace(assetPath))
            {
                return fallback;
            }

            try
            {
                string candidate = assetPath;
                if (!Path.IsPathRooted(candidate))
                {
                    candidate = Path.Combine(mod.Path, candidate);
                }

                candidate = Path.GetFullPath(candidate);
                if (File.Exists(candidate))
                {
                    return NormalizeAssetPath(candidate);
                }
            }
            catch { }

            return fallback;
        }

        internal static List<ManagedMod> GetMods()
        {
            EnsurePaths();
            List<ManagedMod> mods = new List<ManagedMod>();
            AddMods(mods, modsPath, true);
            AddMods(mods, disabledPath, false);
            return mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static string Toggle(ManagedMod mod)
        {
            EnsurePaths();
            if (mod == null)
            {
                return "No mod selected.";
            }

            if (mod.Protected)
            {
                return mod.Name + " is protected.";
            }

            string destinationRoot = mod.Active ? disabledPath : modsPath;
            string destination = UniquePath(Path.Combine(destinationRoot, mod.Name));

            try
            {
                Directory.Move(mod.Path, destination);
                return mod.Name + " moved to " + (mod.Active ? DisabledFolderName + "." : "Mods.");
            }
            catch (Exception ex)
            {
                return "Could not move " + mod.Name + ": " + ex.Message;
            }
        }

        private static void AddMods(List<ManagedMod> mods, string root, bool active)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string path in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (IsHiddenLegacyFolder(name))
                {
                    continue;
                }

                mods.Add(new ManagedMod
                {
                    Name = name,
                    Path = path,
                    Active = active,
                    Protected = IsProtected(name),
                    Info = ReadInfo(path)
                });
            }
        }

        private static bool IsProtected(string name)
        {
            return name.Equals(managerFolderName, StringComparison.OrdinalIgnoreCase)
                || name.Equals("0_TFP_Harmony", StringComparison.OrdinalIgnoreCase)
                || name.Equals("0_ModsManager", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHiddenLegacyFolder(string name)
        {
            return name.StartsWith("000_ModToggle", StringComparison.OrdinalIgnoreCase);
        }

        private static ModInfo ReadInfo(string path)
        {
            ModInfo info = new ModInfo();
            string infoPath = Path.Combine(path, "ModInfo.xml");
            if (!File.Exists(infoPath))
            {
                return info;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(infoPath);
                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    XmlElement element = node as XmlElement;
                    if (element == null || !element.HasAttribute("value"))
                    {
                        continue;
                    }

                    string value = element.GetAttribute("value");
                    if (element.Name.Equals("DisplayName", StringComparison.OrdinalIgnoreCase)) { info.DisplayName = value; }
                    else if (element.Name.Equals("Version", StringComparison.OrdinalIgnoreCase)) { info.Version = value; }
                    else if (element.Name.Equals("Author", StringComparison.OrdinalIgnoreCase)) { info.Author = value; }
                    else if (element.Name.Equals("Description", StringComparison.OrdinalIgnoreCase)) { info.Description = value; }
                    else if (element.Name.Equals("Website", StringComparison.OrdinalIgnoreCase)) { info.Website = value; }
                    else if (element.Name.Equals("UpdateManifest", StringComparison.OrdinalIgnoreCase)) { info.UpdateManifest = value; }
                    else if (element.Name.Equals("GameVersion", StringComparison.OrdinalIgnoreCase)) { info.GameVersion = value; }
                    else if (element.Name.Equals("Changelog", StringComparison.OrdinalIgnoreCase)) { info.Changelog = value; }
                    // Accept vanilla-style names and mod-manager-specific aliases.
                    else if (IsElementName(element, "Icon", "ModIcon")) { info.Icon = value; }
                    else if (IsElementName(element, "Banner", "ModBanner")) { info.Banner = value; }
                }
            }
            catch { }

            info.ConfigFileCount = CountConfigFiles(path);
            info.ConfigSettings = ModConfigManifest.LoadSettings(path);
            if (info.ConfigSettings.Count > 0)
            {
                info.ConfigFileCount = info.ConfigSettings.Count;
            }
            return info;
        }

        private static bool IsElementName(XmlElement element, string name, string alias)
        {
            return element.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || element.Name.Equals(alias, StringComparison.OrdinalIgnoreCase);
        }

        private static int CountConfigFiles(string path)
        {
            return GetConfigFiles(path).Count;
        }

        internal static List<string> GetConfigFiles(string path)
        {
            List<string> results = new List<string>();
            string configPath = Path.Combine(path, "Config");
            if (!Directory.Exists(configPath))
            {
                return results;
            }

            try
            {
                string[] files = Directory.GetFiles(configPath, "*.*", SearchOption.AllDirectories);
                results.AddRange(files.Where(IsEditableConfigFile));
            }
            catch { }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        private static bool IsEditableConfigFile(string file)
        {
            string extension = Path.GetExtension(file);
            if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string name = Path.GetFileNameWithoutExtension(file);
            return name.IndexOf("config", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("options", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string RelativeToMod(ManagedMod mod, string path)
        {
            if (mod == null || string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                string root = mod.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(root.Length);
                }
            }
            catch { }

            return Path.GetFileName(path);
        }

        private static void EnsurePaths()
        {
            if (string.IsNullOrEmpty(modsPath))
            {
                modsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            }

            if (string.IsNullOrEmpty(disabledPath))
            {
                disabledPath = Path.Combine(Directory.GetParent(modsPath)?.FullName ?? AppDomain.CurrentDomain.BaseDirectory, DisabledFolderName);
            }
            if (!Directory.Exists(disabledPath))
            {
                Directory.CreateDirectory(disabledPath);
            }
        }

        private static string UniquePath(string path)
        {
            if (!Directory.Exists(path))
            {
                return path;
            }

            string parent = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(parent, name + "_" + i);
                if (!Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(parent, name + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
        }

        private static string ResolveManagerAsset(string relativePath)
        {
            EnsurePaths();
            string root = managerPath;
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(modsPath, managerFolderName);
            }

            return NormalizeAssetPath(Path.Combine(root, relativePath));
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string ExternalTexturePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "" : "@:" + path;
        }
    }

    internal sealed class ManagedMod
    {
        internal string Name;
        internal string Path;
        internal bool Active;
        internal bool Protected;
        internal ModInfo Info;
    }

    internal sealed class ModInfo
    {
        internal string DisplayName = "";
        internal string Version = "";
        internal string Author = "";
        internal string Description = "";
        internal string Website = "";
        internal string Icon = "";
        internal string Banner = "";
        internal string UpdateManifest = "";
        internal string GameVersion = "";
        internal string Changelog = "";
        internal string LatestVersion = "";
        internal string LatestGameVersion = "";
        internal string LatestChangelog = "";
        internal string UpdateWebsite = "";
        internal UpdateState UpdateState = UpdateState.NotChecked;
        internal string UpdateMessage = "";
        internal int ConfigFileCount;
        internal List<ConfigSetting> ConfigSettings = new List<ConfigSetting>();
    }

    internal sealed class ConfigSetting
    {
        internal string Id = "";
        internal string Label = "";
        internal string Description = "";
        internal string Type = "text";
        internal string Target = "";
        internal string Key = "";
        internal string Mode = "keyValue";
        internal string XPath = "";
        internal string Attribute = "";
        internal string Presets = "";
        internal string PresetFile = "";
        internal string CurrentValue = "";
        internal string PendingValue = "";
        internal string DefaultValue = "";
        internal string Min = "";
        internal string Max = "";
        internal string Step = "";
        internal List<string> Options = new List<string>();
        internal bool RestartRequired = true;

        internal bool IsDirty
        {
            get { return !string.Equals(CurrentValue ?? "", PendingValue ?? "", StringComparison.Ordinal); }
        }

        internal string DisplayValue
        {
            get
            {
                if (Type.Equals("bool", StringComparison.OrdinalIgnoreCase))
                {
                    return IsTruthy(PendingValue) ? "ON" : "OFF";
                }

                return PendingValue ?? "";
            }
        }

        private string PresetOptionDisplayValue()
        {
            string pending = PendingValue ?? "";
            string display = HumanizePresetName(pending);
            int index = Options.FindIndex(o => string.Equals(o, pending, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? display + " (" + (index + 1) + "/" + Options.Count + ")" : display;
        }

        private static string HumanizePresetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string spaced = Regex.Replace(value.Replace('_', ' ').Replace('-', ' '), @"(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[0-9])(?=[A-Za-z])", " ");
            return Regex.Replace(spaced, @"\s+", " ").Trim();
        }
        internal void Adjust(int direction)
        {
            if (Type.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                PendingValue = IsTruthy(PendingValue) ? "false" : "true";
                return;
            }

            if (Type.Equals("choice", StringComparison.OrdinalIgnoreCase) && Options.Count > 0)
            {
                int index = Options.FindIndex(o => string.Equals(o, PendingValue, StringComparison.OrdinalIgnoreCase));
                if (index < 0) { index = 0; }
                index = (index + direction) % Options.Count;
                if (index < 0) { index += Options.Count; }
                PendingValue = Options[index];
                return;
            }

            decimal current;
            if (!decimal.TryParse(PendingValue, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            {
                current = 0m;
            }

            decimal step;
            if (!decimal.TryParse(Step, NumberStyles.Float, CultureInfo.InvariantCulture, out step) || step <= 0m)
            {
                step = Type.Equals("float", StringComparison.OrdinalIgnoreCase) ? 0.1m : 1m;
            }

            current += step * direction;
            decimal min;
            if (decimal.TryParse(Min, NumberStyles.Float, CultureInfo.InvariantCulture, out min) && current < min)
            {
                current = min;
            }

            decimal max;
            if (decimal.TryParse(Max, NumberStyles.Float, CultureInfo.InvariantCulture, out max) && current > max)
            {
                current = max;
            }

            PendingValue = Type.Equals("int", StringComparison.OrdinalIgnoreCase)
                ? decimal.Round(current).ToString(CultureInfo.InvariantCulture)
                : current.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsTruthy(string value)
        {
            return value != null
                && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("on", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class ModConfigManifest
    {
        private const string ManifestName = "ModsManagerConfig.xml";
        private static readonly List<string> DefaultColorOptions = new List<string>
        {
            "#FFFFFF", "#000000", "#808080",
            "#FFB3B3", "#B31919",
            "#FFD8A8", "#CC6500",
            "#FFF0A8", "#B38A00",
            "#B8F5B8", "#1C8F2E",
            "#AEEFEA", "#007D79",
            "#A8D8FF", "#0066CC",
            "#C8B6FF", "#5C2DB3",
            "#F4B4FF", "#9B1FA8",
            "#FFB3D1", "#B3195A"
        };

        private sealed class NaturalPresetComparer : IComparer<string>
        {
            internal static readonly NaturalPresetComparer Instance = new NaturalPresetComparer();

            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y)) { return 0; }
                if (x == null) { return -1; }
                if (y == null) { return 1; }

                int ix = 0;
                int iy = 0;
                while (ix < x.Length && iy < y.Length)
                {
                    char cx = x[ix];
                    char cy = y[iy];
                    if (char.IsDigit(cx) && char.IsDigit(cy))
                    {
                        long nx = ReadNumber(x, ref ix);
                        long ny = ReadNumber(y, ref iy);
                        int numberCompare = nx.CompareTo(ny);
                        if (numberCompare != 0) { return numberCompare; }
                        continue;
                    }

                    int charCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                    if (charCompare != 0) { return charCompare; }
                    ix++;
                    iy++;
                }

                return x.Length.CompareTo(y.Length);
            }

            private static long ReadNumber(string value, ref int index)
            {
                long number = 0;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    number = Math.Min(999999999L, (number * 10) + (value[index] - '0'));
                    index++;
                }

                return number;
            }
        }
        internal static List<ConfigSetting> LoadSettings(string modPath)
        {
            List<ConfigSetting> settings = new List<ConfigSetting>();
            if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
            {
                return settings;
            }

            foreach (string manifest in FindManifests(modPath))
            {
                try
                {
                    XmlDocument document = new XmlDocument();
                    document.Load(manifest);
                    foreach (XmlNode node in document.SelectNodes("//Setting"))
                    {
                        XmlElement element = node as XmlElement;
                        if (element == null)
                        {
                            continue;
                        }

                        ConfigSetting setting = ReadSetting(modPath, element);
                        if (setting != null)
                        {
                            settings.Add(setting);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[ModsManager] Could not read config manifest '" + manifest + "': " + ex.Message);
                }
            }

            return settings;
        }

        internal static bool SaveSettings(string modPath, IEnumerable<ConfigSetting> settings, out string message)
        {
            message = "";
            if (settings == null)
            {
                message = "No settings to save.";
                return false;
            }

            Dictionary<string, List<ConfigSetting>> byTarget = settings
                .Where(s => s != null && s.IsDirty)
                .GroupBy(s => BuildSaveGroupKey(modPath, s), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            if (byTarget.Count == 0)
            {
                message = "No config changes to save.";
                return false;
            }

            int saved = 0;
            foreach (KeyValuePair<string, List<ConfigSetting>> entry in byTarget)
            {
                string mode;
                string targetPath;
                SplitSaveGroupKey(entry.Key, out mode, out targetPath);
                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                {
                    message = "Config target was not found.";
                    return false;
                }

                string backupPath = targetPath + ".modsmanager.bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(targetPath, backupPath);
                }

                try
                {
                    if (mode.Equals("xml", StringComparison.OrdinalIgnoreCase))
                    {
                        SaveXmlFile(targetPath, entry.Value);
                    }
                    else if (mode.Equals("preset", StringComparison.OrdinalIgnoreCase))
                    {
                        SavePresetFile(modPath, targetPath, entry.Value);
                    }
                    else
                    {
                        SaveKeyValueFile(targetPath, entry.Value);
                    }
                }
                catch (Exception ex)
                {
                    message = "Could not save config: " + ex.Message;
                    return false;
                }

                foreach (ConfigSetting setting in entry.Value)
                {
                    setting.CurrentValue = setting.PendingValue;
                    saved++;
                }
            }

            message = saved == 1 ? "Saved 1 config setting." : "Saved " + saved + " config settings.";
            return true;
        }

        private static List<string> FindManifests(string modPath)
        {
            try
            {
                return Directory.GetFiles(modPath, ManifestName, SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static ConfigSetting ReadSetting(string modPath, XmlElement element)
        {
            string target = Attr(element, "target");
            string key = Attr(element, "key");
            string mode = NormalizeMode(Attr(element, "mode"));
            string xPath = Attr(element, "xpath");
            string attribute = Attr(element, "attribute");
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            if (mode.Equals("xml", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(xPath))
                {
                    return null;
                }
            }
            else if (mode.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                // Preset settings copy a file from Presets/<choice>/ into the target.
            }
            else if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string targetPath = ResolveTargetPath(modPath, target);
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            {
                return null;
            }

            ConfigSetting setting = new ConfigSetting
            {
                Id = FirstNonEmpty(Attr(element, "id"), key),
                Label = FirstNonEmpty(Attr(element, "label"), key),
                Description = Attr(element, "description"),
                Type = FirstNonEmpty(Attr(element, "type"), "text"),
                Target = target,
                Key = FirstNonEmpty(key, FirstNonEmpty(xPath, Attr(element, "id"))),
                Mode = mode,
                XPath = xPath,
                Attribute = attribute,
                Presets = FirstNonEmpty(Attr(element, "presets"), "Presets"),
                PresetFile = Attr(element, "presetFile"),
                DefaultValue = Attr(element, "default"),
                Min = Attr(element, "min"),
                Max = Attr(element, "max"),
                Step = Attr(element, "step"),
                RestartRequired = !Attr(element, "restartRequired").Equals("false", StringComparison.OrdinalIgnoreCase)
            };

            string options = Attr(element, "options");
            if (!string.IsNullOrWhiteSpace(options))
            {
                setting.Options = options.Split(',')
                    .Select(o => o.Trim())
                    .Where(o => o.Length > 0)
                    .ToList();
                if (setting.Options.Count > 0 && setting.Type.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    setting.Type = "choice";
                }
            }
            if (setting.Mode.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                setting.Type = "choice";
                if (string.IsNullOrWhiteSpace(setting.PresetFile))
                {
                    setting.PresetFile = Path.GetFileName(setting.Target);
                }

                if (setting.Options.Count == 0)
                {
                    setting.Options = DiscoverPresetOptions(modPath, setting);
                }

                if (setting.Options.Count == 0)
                {
                    return null;
                }
            }
            else if (setting.Type.Equals("color", StringComparison.OrdinalIgnoreCase))
            {
                setting.Options = DefaultColorOptions.ToList();
                setting.Type = "choice";
            }

            setting.CurrentValue = mode.Equals("xml", StringComparison.OrdinalIgnoreCase)
                ? ReadXmlValue(targetPath, setting)
                : mode.Equals("preset", StringComparison.OrdinalIgnoreCase)
                    ? ReadPresetValue(modPath, targetPath, setting)
                    : ReadKeyValue(targetPath, key, setting.DefaultValue);
            setting.PendingValue = setting.CurrentValue;
            if (setting.Options.Count == 0 && IsHexColor(setting.CurrentValue))
            {
                setting.Options = DefaultColorOptions.ToList();
                setting.Type = "choice";
            }

            return setting;
        }

        private static bool IsHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
            {
                return false;
            }

            if (value.Length != 7 && value.Length != 9)
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ResolveTargetPath(string modPath, string target)
        {
            try
            {
                string candidate = Path.IsPathRooted(target) ? target : Path.Combine(modPath, target);
                string full = Path.GetFullPath(candidate);
                string root = Path.GetFullPath(modPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : "";
            }
            catch
            {
                return "";
            }
        }

        private static string ReadKeyValue(string path, string key, string defaultValue)
        {
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string currentKey = line.Substring(0, equals).Trim();
                if (currentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(equals + 1).Trim();
                }
            }

            return defaultValue ?? "";
        }

        private static List<string> DiscoverPresetOptions(string modPath, ConfigSetting setting)
        {
            List<string> options = new List<string>();
            string presetsRoot = ResolveTargetPath(modPath, setting.Presets);
            if (string.IsNullOrWhiteSpace(presetsRoot) || !Directory.Exists(presetsRoot))
            {
                return options;
            }

            string presetFile = FirstNonEmpty(setting.PresetFile, Path.GetFileName(setting.Target));
            foreach (string directory in Directory.GetDirectories(presetsRoot).OrderBy(p => Path.GetFileName(p), NaturalPresetComparer.Instance))
            {
                string candidate = Path.Combine(directory, presetFile);
                if (File.Exists(candidate))
                {
                    options.Add(Path.GetFileName(directory));
                }
            }

            return options;
        }

        private static string ReadPresetValue(string modPath, string targetPath, ConfigSetting setting)
        {
            foreach (string option in setting.Options)
            {
                string presetPath = ResolvePresetFilePath(modPath, setting, option);
                if (!string.IsNullOrWhiteSpace(presetPath) && File.Exists(presetPath) && FilesMatch(targetPath, presetPath))
                {
                    return option;
                }
            }

            if (!string.IsNullOrWhiteSpace(setting.DefaultValue) && setting.Options.Any(o => o.Equals(setting.DefaultValue, StringComparison.OrdinalIgnoreCase)))
            {
                return setting.DefaultValue;
            }

            return setting.Options.Count > 0 ? setting.Options[0] : "";
        }

        private static bool FilesMatch(string leftPath, string rightPath)
        {
            string left = NormalizePresetText(File.ReadAllText(leftPath));
            string right = NormalizePresetText(File.ReadAllText(rightPath));
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        private static string NormalizePresetText(string value)
        {
            string normalized = (value ?? "").Replace("\r\n", "\n").Trim();
            return Regex.Replace(normalized, @">\s+<", "><");
        }

        private static string ResolvePresetFilePath(string modPath, ConfigSetting setting, string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                return "";
            }

            string presetsRoot = ResolveTargetPath(modPath, setting.Presets);
            if (string.IsNullOrWhiteSpace(presetsRoot))
            {
                return "";
            }

            string presetFile = FirstNonEmpty(setting.PresetFile, Path.GetFileName(setting.Target));
            string candidate = Path.Combine(presetsRoot, presetName, presetFile);
            string full = Path.GetFullPath(candidate);
            string root = Path.GetFullPath(modPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : "";
        }

        private static void SavePresetFile(string modPath, string targetPath, List<ConfigSetting> settings)
        {
            foreach (ConfigSetting setting in settings)
            {
                string sourcePath = ResolvePresetFilePath(modPath, setting, setting.PendingValue);
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    throw new InvalidOperationException("Preset was not found: " + setting.PendingValue);
                }

                File.Copy(sourcePath, targetPath, true);
            }
        }
        private static void SaveKeyValueFile(string path, List<ConfigSetting> settings)
        {
            List<string> lines = File.ReadAllLines(path).ToList();
            HashSet<string> savedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                string rawLine = lines[i];
                string trimmed = rawLine.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                {
                    continue;
                }

                int equals = rawLine.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string currentKey = rawLine.Substring(0, equals).Trim();
                ConfigSetting setting = settings.FirstOrDefault(s => s.Key.Equals(currentKey, StringComparison.OrdinalIgnoreCase));
                if (setting == null)
                {
                    continue;
                }

                string prefix = rawLine.Substring(0, equals + 1);
                lines[i] = prefix + setting.PendingValue;
                savedKeys.Add(setting.Key);
            }

            foreach (ConfigSetting setting in settings.Where(s => !savedKeys.Contains(s.Key)))
            {
                lines.Add(setting.Key + "=" + setting.PendingValue);
            }

            File.WriteAllLines(path, lines.ToArray());
        }

        private static string BuildSaveGroupKey(string modPath, ConfigSetting setting)
        {
            return NormalizeMode(setting.Mode) + "|" + ResolveTargetPath(modPath, setting.Target);
        }

        private static void SplitSaveGroupKey(string key, out string mode, out string targetPath)
        {
            int divider = key.IndexOf('|');
            if (divider < 0)
            {
                mode = "keyValue";
                targetPath = key;
                return;
            }

            mode = key.Substring(0, divider);
            targetPath = key.Substring(divider + 1);
        }

        private static string NormalizeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return "keyValue";
            }

            if (mode.Equals("xml", StringComparison.OrdinalIgnoreCase))
            {
                return "xml";
            }

            if (mode.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                return "preset";
            }

            return "keyValue";
        }

        private static string ReadXmlValue(string path, ConfigSetting setting)
        {
            XmlDocument document = new XmlDocument();
            document.PreserveWhitespace = true;
            document.Load(path);

            XmlNode node = SelectXmlNode(document, setting);
            if (node == null)
            {
                return setting.DefaultValue ?? "";
            }

            XmlAttribute attribute = ResolveXmlAttribute(node, setting.Attribute);
            if (attribute != null)
            {
                return attribute.Value;
            }

            return node is XmlElement ? ((XmlElement)node).InnerText : (node.Value ?? setting.DefaultValue ?? "");
        }

        private static void SaveXmlFile(string path, List<ConfigSetting> settings)
        {
            XmlDocument document = new XmlDocument();
            document.PreserveWhitespace = true;
            document.Load(path);

            foreach (ConfigSetting setting in settings)
            {
                XmlNode node = SelectXmlNode(document, setting);
                if (node == null)
                {
                    throw new InvalidOperationException("XPath was not found: " + setting.XPath);
                }

                XmlAttribute attribute = ResolveXmlAttribute(node, setting.Attribute);
                if (attribute != null)
                {
                    attribute.Value = setting.PendingValue ?? "";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(setting.Attribute))
                {
                    XmlElement element = node as XmlElement;
                    if (element == null)
                    {
                        throw new InvalidOperationException("XPath did not select an element for attribute: " + setting.XPath);
                    }

                    element.SetAttribute(setting.Attribute, setting.PendingValue ?? "");
                    continue;
                }

                if (node is XmlElement)
                {
                    ((XmlElement)node).InnerText = setting.PendingValue ?? "";
                }
                else
                {
                    node.Value = setting.PendingValue ?? "";
                }
            }

            XmlWriterSettings writerSettings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(path, writerSettings))
            {
                document.Save(writer);
            }
        }

        private static XmlNode SelectXmlNode(XmlDocument document, ConfigSetting setting)
        {
            if (document == null || setting == null || string.IsNullOrWhiteSpace(setting.XPath))
            {
                return null;
            }

            return document.SelectSingleNode(setting.XPath);
        }

        private static XmlAttribute ResolveXmlAttribute(XmlNode node, string attributeName)
        {
            XmlAttribute selectedAttribute = node as XmlAttribute;
            if (selectedAttribute != null)
            {
                return selectedAttribute;
            }

            if (string.IsNullOrWhiteSpace(attributeName))
            {
                return null;
            }

            XmlElement element = node as XmlElement;
            return element == null ? null : element.GetAttributeNode(attributeName);
        }

        private static string Attr(XmlElement element, string name)
        {
            return element.HasAttribute(name) ? element.GetAttribute(name).Trim() : "";
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }
    }

    internal enum UpdateState
    {
        NotChecked,
        NoManifest,
        Checking,
        UpToDate,
        UpdateAvailable,
        Error
    }

    internal static class UpdateChecker
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedUpdate> Cache = new Dictionary<string, CachedUpdate>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        internal static void CheckAll(IEnumerable<ManagedMod> mods, bool forceRefresh)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            foreach (ManagedMod mod in mods.Where(m => !string.IsNullOrWhiteSpace(m.Info?.UpdateManifest)))
            {
                Check(mod, forceRefresh);
                Thread.Sleep(250);
            }

            foreach (ManagedMod mod in mods.Where(m => string.IsNullOrWhiteSpace(m.Info?.UpdateManifest)))
            {
                mod.Info.UpdateState = UpdateState.NoManifest;
                mod.Info.UpdateMessage = "No update manifest listed.";
            }
        }

        private static void Check(ManagedMod mod, bool forceRefresh)
        {
            ModInfo info = mod.Info;
            CachedUpdate cached;
            if (!forceRefresh && TryGetCached(info.UpdateManifest, out cached))
            {
                ApplyCached(info, cached);
                return;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(DownloadString(info.UpdateManifest, forceRefresh));
                Dictionary<string, string> values = ReadValues(document);
                string latestVersion = Get(values, "Version");
                string latestGameVersion = Get(values, "GameVersion");
                if (string.IsNullOrWhiteSpace(latestVersion) && string.IsNullOrWhiteSpace(latestGameVersion))
                {
                    cached = CachedUpdate.Error("Manifest does not list Version or GameVersion.");
                    SetCached(info.UpdateManifest, cached);
                    ApplyCached(info, cached);
                    return;
                }

                cached = new CachedUpdate
                {
                    State = UpdateState.UpToDate,
                    LatestVersion = latestVersion,
                    LatestGameVersion = latestGameVersion,
                    LatestChangelog = Get(values, "Changelog"),
                    UpdateWebsite = FirstNonEmpty(Get(values, "Website"), Get(values, "DownloadUrl")),
                    Message = "Manifest loaded."
                };
                SetCached(info.UpdateManifest, cached);
                ApplyCached(info, cached);
            }
            catch (Exception ex)
            {
                cached = CachedUpdate.Error("Update check failed: " + ex.Message);
                SetCached(info.UpdateManifest, cached);
                ApplyCached(info, cached);
                Log.Warning("[ModsManager] Update check failed for " + mod.Name + ": " + ex.Message);
            }
        }

        private static bool TryGetCached(string url, out CachedUpdate update)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(url, out update) && DateTime.UtcNow - update.CheckedAt < CacheDuration)
                {
                    return true;
                }
            }

            update = null;
            return false;
        }

        private static void SetCached(string url, CachedUpdate update)
        {
            update.CheckedAt = DateTime.UtcNow;
            lock (CacheLock)
            {
                Cache[url] = update;
            }
        }

        private static void ApplyCached(ModInfo info, CachedUpdate update)
        {
            info.LatestVersion = update.LatestVersion;
            info.LatestGameVersion = update.LatestGameVersion;
            info.LatestChangelog = update.LatestChangelog;
            info.UpdateWebsite = update.UpdateWebsite;

            if (update.State == UpdateState.Error)
            {
                info.UpdateState = UpdateState.Error;
                info.UpdateMessage = update.Message;
                return;
            }

            int versionComparison = string.IsNullOrWhiteSpace(update.LatestVersion)
                ? 0
                : CompareVersions(info.Version, update.LatestVersion);
            int gameVersionComparison = string.IsNullOrWhiteSpace(update.LatestGameVersion)
                ? 0
                : CompareVersions(info.GameVersion, update.LatestGameVersion);

            if (versionComparison < 0 && gameVersionComparison < 0)
            {
                info.UpdateState = UpdateState.UpdateAvailable;
                info.UpdateMessage = "Update available: " + update.LatestVersion + " for game " + update.LatestGameVersion;
            }
            else if (versionComparison < 0)
            {
                info.UpdateState = UpdateState.UpdateAvailable;
                info.UpdateMessage = "Update available: " + update.LatestVersion;
            }
            else if (gameVersionComparison < 0)
            {
                info.UpdateState = UpdateState.UpdateAvailable;
                info.UpdateMessage = "Game version update available: " + update.LatestGameVersion;
            }
            else
            {
                info.UpdateState = UpdateState.UpToDate;
                info.UpdateMessage = "Up to date.";
            }
        }

        private static string DownloadString(string url, bool forceRefresh)
        {
            string requestUrl = forceRefresh ? WithCacheBust(url) : url;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestUrl);
            request.Method = "GET";
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;
            request.UserAgent = "7D2D ModsManager";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string WithCacheBust(string url)
        {
            string separator = url.Contains("?") ? "&" : "?";
            return url + separator + "modManagerCacheBust=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static Dictionary<string, string> ReadValues(XmlDocument document)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (document.DocumentElement == null)
            {
                return values;
            }

            foreach (XmlNode node in document.DocumentElement.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element != null && element.HasAttribute("value"))
                {
                    values[element.Name] = element.GetAttribute("value");
                }
            }

            return values;
        }

        private static string Get(Dictionary<string, string> values, string name)
        {
            string value;
            return values.TryGetValue(name, out value) ? value : "";
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }

        internal static int CompareVersions(string installed, string latest)
        {
            Version installedVersion;
            Version latestVersion;
            if (TryParseVersion(installed, out installedVersion) && TryParseVersion(latest, out latestVersion))
            {
                return installedVersion.CompareTo(latestVersion);
            }

            return string.Compare(installed ?? "", latest ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string cleaned = value.Trim();
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(1);
            }

            int suffix = cleaned.IndexOfAny(new[] { '-', '+', ' ' });
            if (suffix >= 0)
            {
                cleaned = cleaned.Substring(0, suffix);
            }

            string[] parts = cleaned.Split('.');
            while (parts.Length < 2)
            {
                cleaned += ".0";
                parts = cleaned.Split('.');
            }

            return Version.TryParse(cleaned, out version);
        }

        private sealed class CachedUpdate
        {
            internal UpdateState State;
            internal string LatestVersion = "";
            internal string LatestGameVersion = "";
            internal string LatestChangelog = "";
            internal string UpdateWebsite = "";
            internal string Message = "";
            internal DateTime CheckedAt;

            internal static CachedUpdate Error(string message)
            {
                return new CachedUpdate
                {
                    State = UpdateState.Error,
                    Message = message
                };
            }
        }
    }
}
