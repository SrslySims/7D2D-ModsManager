using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SrslyModsManager
{
    public sealed class ModManagerWindow : XUiController
    {
        private const int RowCount = 10;
        private const int ConfigRowCount = 10;
        private readonly List<ManagedMod> mods = new List<ManagedMod>();
        private string status = "Changes take effect after a full game restart.";
        private string search = "";
        private int selectedIndex;
        private int topIndex;
        private XUiC_TextInput searchInput;
        private readonly XUiC_TextInput[] configInputs = new XUiC_TextInput[ConfigRowCount];
        private XUiV_Texture selectedIconView;
        private XUiV_Texture selectedBannerView;
        private readonly Dictionary<string, UnityEngine.Texture2D> textureCache = new Dictionary<string, UnityEngine.Texture2D>(StringComparer.OrdinalIgnoreCase);
        private string currentIconPath = "";
        private string currentBannerPath = "";
        private bool updateCheckRunning;
        private volatile bool updateRefreshPending;
        private volatile bool restartRequired;
        private bool configTabActive;
        private int bodyScrollIndex;
        private int configTopIndex;
        private bool closingToMainMenu;

        public override void Init()
        {
            base.Init();
            HookButton("btnBack", Close);
            HookButton("btnRefresh", () => RefreshList(true, true));
            HookButton("btnPrev", PreviousPage);
            HookButton("btnNext", NextPage);
            HookButton("btnScrollPrev", ScrollUpOne);
            HookButton("btnScrollNext", ScrollDownOne);
            HookButton("btnSelectedAction", ToggleSelected);
            HookButton("btnRestart", RestartGame);
            HookButton("btnOpenWebsite", OpenSelectedWebsite);
            HookButton("btnDetailsTab", ShowDetailsTab);
            HookButton("btnConfigTab", ShowConfigTab);
            HookButton("btnConfigSave", SaveConfigSettings);
            HookButton("btnConfigPrev", PreviousConfigPage);
            HookButton("btnConfigNext", NextConfigPage);
            HookButton("btnConfigScrollPrev", ScrollConfigUpOne);
            HookButton("btnConfigScrollNext", ScrollConfigDownOne);
            HookScroll("listPanel", ScrollList);
            HookScroll("rows", ScrollList);
            HookScroll("btnScrollPrev", ScrollList);
            HookScroll("btnScrollNext", ScrollList);
            HookScroll("detailPanel", ScrollRightPanel);
            HookScroll("bodyText", ScrollBody);
            HookScroll("configTab", ScrollConfig);
            HookScroll("configEditor", ScrollConfig);
            HookScroll("configNotice", ScrollConfig);
            HookScroll("btnConfigScrollPrev", ScrollConfig);
            HookScroll("btnConfigScrollNext", ScrollConfig);

            XUiController searchController = GetChildById("searchInput");
            searchInput = searchController as XUiC_TextInput;
            if (searchInput == null && searchController != null)
            {
                searchInput = searchController.GetChildByType<XUiC_TextInput>();
            }
            if (searchInput != null)
            {
                searchInput.OnChangeHandler += OnSearchChanged;
            }

            selectedIconView = GetChildById("selectedIcon")?.ViewComponent as XUiV_Texture;
            selectedBannerView = GetChildById("selectedBanner")?.ViewComponent as XUiV_Texture;

            for (int i = 0; i < RowCount; i++)
            {
                int index = i;
                HookButton("btnSelect" + i, () => SelectRow(index));
                HookButton("btnToggle" + i, () => Toggle(index));
                HookScroll("btnSelect" + i, ScrollList);
                HookScroll("btnToggle" + i, ScrollList);
            }

            for (int i = 0; i < ConfigRowCount; i++)
            {
                int index = i;
                HookButton("btnConfigDec" + i, () => AdjustConfig(index, -1));
                HookButton("btnConfigInc" + i, () => AdjustConfig(index, 1));
                HookScroll("btnConfigDec" + i, ScrollConfig);
                HookScroll("btnConfigInc" + i, ScrollConfig);
                HookConfigInput("configInput" + i, index);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            configTabActive = false;
            bodyScrollIndex = 0;
            configTopIndex = 0;
            RefreshList(true, false);
        }

        public override void OnClose()
        {
            configTabActive = false;
            configTopIndex = 0;
            base.OnClose();
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            if (bindingName == "managerstatus") { value = status; return true; }
            if (bindingName == "pageinfo")
            {
                int pageCount = PageCount();
                value = pageCount == 0 ? "0 / 0" : (CurrentPage() + 1) + " / " + pageCount;
                return true;
            }
            if (bindingName == "prevpageenabled") { value = topIndex > 0 ? "true" : "false"; return true; }
            if (bindingName == "nextpageenabled") { value = topIndex < MaxTopIndex() ? "true" : "false"; return true; }
            if (bindingName == "scrollhandleheight") { value = ScrollHandleHeight().ToString(); return true; }
            if (bindingName == "scrollhandlepos") { value = ScrollHandlePos().ToString(); return true; }
            if (bindingName == "totalcount") { value = mods.Count.ToString(); return true; }
            if (bindingName == "activecount") { value = mods.Count(m => m.Active).ToString(); return true; }
            if (bindingName == "disabledcount") { value = mods.Count(m => !m.Active).ToString(); return true; }
            if (bindingName == "protectedcount") { value = mods.Count(m => m.Protected).ToString(); return true; }
            if (bindingName == "searchsummary")
            {
                int count = FilteredMods().Count;
                value = string.IsNullOrWhiteSpace(search) ? count + " mods" : count + " matches";
                return true;
            }

            if (bindingName.StartsWith("selected"))
            {
                return GetSelectedBinding(ref value, bindingName);
            }

            if (bindingName.StartsWith("config")
                || bindingName.StartsWith("details")
                || bindingName == "bodytextvisible"
                || bindingName == "configeditorvisible")
            {
                return GetConfigBinding(ref value, bindingName);
            }

            if (bindingName.StartsWith("mod"))
            {
                return GetRowBinding(ref value, bindingName);
            }

            return base.GetBindingValueInternal(ref value, bindingName);
        }

        private void RefreshList(bool checkUpdates, bool forceUpdateCheck = false)
        {
            string selectedPath = CurrentSelected()?.Path;
            mods.Clear();
            mods.AddRange(ModFolders.GetMods());
            if (checkUpdates)
            {
                StartUpdateChecks(forceUpdateCheck);
            }

            List<ManagedMod> filtered = FilteredMods();
            if (!string.IsNullOrEmpty(selectedPath))
            {
                int found = filtered.FindIndex(m => string.Equals(m.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
                if (found >= 0)
                {
                    selectedIndex = found;
                }
            }

            ClampPaging();
            status = mods.Count == 0 ? Localization.Get("xuiModManagerEmpty") : status;
            RefreshUi();
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            HandleKeyboard();
            if (updateRefreshPending)
            {
                updateRefreshPending = false;
                RefreshUi();
            }
        }

        private void HandleKeyboard()
        {
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
            {
                Close();
                return;
            }

            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.UpArrow))
            {
                MoveSelection(-1);
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.DownArrow))
            {
                MoveSelection(1);
            }
        }

        private void OnSearchChanged(XUiController sender, string text, bool changeFromCode)
        {
            search = text ?? "";
            topIndex = 0;
            selectedIndex = 0;
            ClampPaging();
            RefreshUi();
        }

        private void SelectRow(int row)
        {
            int modIndex = topIndex + row;
            if (modIndex < 0 || modIndex >= FilteredMods().Count)
            {
                return;
            }

            selectedIndex = modIndex;
            bodyScrollIndex = 0;
            configTopIndex = 0;
            if ((CurrentSelected()?.Info?.ConfigSettings?.Count ?? 0) == 0)
            {
                configTabActive = false;
            }

            RefreshUi();
        }

        private void Toggle(int row)
        {
            SelectRow(row);
            ToggleSelected();
        }

        private void ToggleSelected()
        {
            ManagedMod mod = CurrentSelected();
            if (mod == null)
            {
                return;
            }

            restartRequired = true;
            status = ModFolders.Toggle(mod) + " " + Localization.Get("xuiModManagerRestart");
            RefreshList(false);
        }

        private void RestartGame()
        {
            try
            {
                string root = System.IO.Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
                string exe = string.IsNullOrEmpty(root) ? "" : System.IO.Path.Combine(root, "7DaysToDie.exe");
                if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
                {
                    System.Diagnostics.Process.Start(exe);
                }

                UnityEngine.Application.Quit();
            }
            catch (Exception ex)
            {
                status = "Could not restart automatically: " + ex.Message;
                RefreshUi();
            }
        }

        private void OpenSelectedWebsite()
        {
            ManagedMod mod = CurrentSelected();
            string website = FirstNonEmpty(mod?.Info?.UpdateWebsite, mod?.Info?.Website);
            if (string.IsNullOrWhiteSpace(website))
            {
                return;
            }

            UnityEngine.Application.OpenURL(website);
        }

        private void ShowDetailsTab()
        {
            configTabActive = false;
            bodyScrollIndex = 0;
            configTopIndex = 0;
            RefreshUi();
        }

        private void ShowConfigTab()
        {
            if ((CurrentSelected()?.Info?.ConfigSettings?.Count ?? 0) == 0)
            {
                return;
            }

            configTabActive = true;
            bodyScrollIndex = 0;
            configTopIndex = 0;
            RefreshUi();
        }

        private void PreviousConfigPage()
        {
            if (configTopIndex <= 0)
            {
                return;
            }

            configTopIndex = Math.Max(0, configTopIndex - ConfigRowCount);
            RefreshUi();
        }

        private void NextConfigPage()
        {
            int max = MaxConfigTopIndex(CurrentSelected());
            if (configTopIndex >= max)
            {
                return;
            }

            configTopIndex = Math.Min(max, configTopIndex + ConfigRowCount);
            RefreshUi();
        }

        private void ScrollConfig(float delta)
        {
            if (delta > 0f)
            {
                ScrollConfigUpOne();
            }
            else if (delta < 0f)
            {
                ScrollConfigDownOne();
            }
        }

        private void ScrollConfigUpOne()
        {
            if (configTopIndex <= 0)
            {
                return;
            }

            configTopIndex--;
            RefreshUi();
        }

        private void ScrollConfigDownOne()
        {
            int max = MaxConfigTopIndex(CurrentSelected());
            if (configTopIndex >= max)
            {
                return;
            }

            configTopIndex++;
            RefreshUi();
        }

        private void AdjustConfig(int row, int direction)
        {
            ManagedMod mod = CurrentSelected();
            ConfigSetting setting = ConfigSettingAt(mod, configTopIndex + row);
            if (setting == null)
            {
                return;
            }

            setting.Adjust(direction);
            RefreshUi();
        }

        private void OnConfigInputChanged(int row, string text, bool changeFromCode)
        {
            if (changeFromCode)
            {
                return;
            }

            ManagedMod mod = CurrentSelected();
            ConfigSetting setting = ConfigSettingAt(mod, configTopIndex + row);
            if (setting == null)
            {
                return;
            }

            setting.PendingValue = text ?? "";
            RefreshUi();
        }

        private void SaveConfigSettings()
        {
            ManagedMod mod = CurrentSelected();
            if (mod == null || mod.Info == null || mod.Info.ConfigSettings.Count == 0)
            {
                return;
            }

            string message;
            if (ModConfigManifest.SaveSettings(mod.Path, mod.Info.ConfigSettings, out message))
            {
                restartRequired = true;
                status = message + " " + Localization.Get("xuiModManagerRestart");
            }
            else
            {
                status = message;
            }

            RefreshUi();
        }

        private void PreviousPage()
        {
            if (topIndex <= 0) { return; }
            topIndex = Math.Max(0, topIndex - RowCount);
            selectedIndex = topIndex;
            RefreshUi();
        }

        private void NextPage()
        {
            int maxTop = MaxTopIndex();
            if (topIndex >= maxTop) { return; }
            topIndex = Math.Min(maxTop, topIndex + RowCount);
            selectedIndex = topIndex;
            RefreshUi();
        }

        private void ScrollList(float delta)
        {
            if (delta > 0f)
            {
                ScrollUpOne();
            }
            else if (delta < 0f)
            {
                ScrollDownOne();
            }
        }

        private void ScrollUpOne()
        {
            if (topIndex <= 0) { return; }
            topIndex--;
            selectedIndex = Math.Max(0, selectedIndex - 1);
            RefreshUi();
        }

        private void ScrollDownOne()
        {
            int maxTop = MaxTopIndex();
            if (topIndex >= maxTop) { return; }
            topIndex++;
            selectedIndex = Math.Min(FilteredMods().Count - 1, selectedIndex + 1);
            RefreshUi();
        }

        private void ScrollBody(float delta)
        {
            List<string> lines = CurrentBodyLines();
            int max = Math.Max(0, lines.Count - BodyVisibleRows());
            if (delta > 0f)
            {
                bodyScrollIndex = Math.Max(0, bodyScrollIndex - 1);
            }
            else if (delta < 0f)
            {
                bodyScrollIndex = Math.Min(max, bodyScrollIndex + 1);
            }

            RefreshUi();
        }

        private void ScrollRightPanel(float delta)
        {
            if (configTabActive)
            {
                ScrollConfig(delta);
            }
            else
            {
                ScrollBody(delta);
            }
        }

        private void MoveSelection(int delta)
        {
            List<ManagedMod> filtered = FilteredMods();
            if (filtered.Count == 0)
            {
                return;
            }

            selectedIndex = Math.Max(0, Math.Min(filtered.Count - 1, selectedIndex + delta));
            bodyScrollIndex = 0;
            configTopIndex = 0;
            if (selectedIndex < topIndex)
            {
                topIndex = selectedIndex;
            }
            else if (selectedIndex >= topIndex + RowCount)
            {
                topIndex = selectedIndex - RowCount + 1;
            }

            ClampPaging();
            if ((CurrentSelected()?.Info?.ConfigSettings?.Count ?? 0) == 0)
            {
                configTabActive = false;
            }

            RefreshUi();
        }

        private void Close()
        {
            if (xui?.playerUI?.windowManager != null)
            {
                if (closingToMainMenu)
                {
                    return;
                }

                closingToMainMenu = true;
                try
                {
                    if (xui.playerUI.windowManager.IsWindowOpen(ModFolders.WindowGroup))
                    {
                        xui.playerUI.windowManager.Close(ModFolders.WindowGroup);
                    }

                    xui.playerUI.windowManager.Open("mainMenu", true, false);
                }
                finally
                {
                    closingToMainMenu = false;
                }
            }
        }

        private void HookButton(string name, System.Action action)
        {
            XUiController child = GetChildById(name);
            XUiController controller = child?.ViewComponent?.Controller;
            if (controller != null)
            {
                controller.OnPress += delegate { action(); };
            }
        }

        private void HookScroll(string name, System.Action<float> action)
        {
            XUiController child = GetChildById(name);
            XUiController controller = child?.ViewComponent?.Controller;
            if (controller != null)
            {
                controller.OnScroll += delegate (XUiController sender, float delta) { action(delta); };
            }
        }

        private void HookConfigInput(string name, int row)
        {
            XUiController child = GetChildById(name);
            XUiC_TextInput input = child as XUiC_TextInput;
            if (input == null && child != null)
            {
                input = child.GetChildByType<XUiC_TextInput>();
            }

            if (input != null)
            {
                configInputs[row] = input;
                input.OnChangeHandler += (sender, text, changeFromCode) => OnConfigInputChanged(row, text, changeFromCode);
            }
        }

        private void RefreshUi()
        {
            RefreshBindings();
            ApplySelectedImages();
        }

        private void ApplySelectedImages()
        {
            ManagedMod mod = CurrentSelected();
            string iconPath = IconFilePath(mod);
            string bannerPath = BannerFilePath(mod);

            if (selectedIconView != null && !string.Equals(currentIconPath, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedIconView.Texture = LoadTexture(iconPath);
                currentIconPath = iconPath;
            }

            if (selectedBannerView != null && !string.Equals(currentBannerPath, bannerPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedBannerView.Texture = LoadTexture(bannerPath);
                currentBannerPath = bannerPath;
            }
        }

        private UnityEngine.Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            UnityEngine.Texture2D cached;
            if (textureCache.TryGetValue(path, out cached))
            {
                return cached;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                UnityEngine.Texture2D texture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
                if (!UnityEngine.ImageConversion.LoadImage(texture, bytes))
                {
                    return null;
                }

                texture.name = Path.GetFileName(path);
                texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                texture.filterMode = UnityEngine.FilterMode.Bilinear;
                textureCache[path] = texture;
                return texture;
            }
            catch (Exception ex)
            {
                Log.Warning("[ModsManager] Could not load image '" + path + "': " + ex.Message);
                return null;
            }
        }

        private bool GetSelectedBinding(ref string value, string bindingName)
        {
            ManagedMod mod = CurrentSelected();
            if (mod == null)
            {
                switch (bindingName)
                {
                    case "selectedcolor":
                    case "selectedupdatecolor":
                        value = "170,170,170,255";
                        return true;
                    case "selectedwebsiteenabled":
                    case "selectedconfigenabled":
                    case "selectedactionenabled":
                        value = "false";
                        return true;
                }

                value = "";
                return true;
            }

            switch (bindingName)
            {
                case "selecteddisplay": value = DisplayName(mod); return true;
                case "selectedfolder": value = mod.Name; return true;
                case "selectedversion": value = VersionLine(mod); return true;
                case "selectedgameversion": value = GameVersionLine(mod); return true;
                case "selectedauthor": value = string.IsNullOrWhiteSpace(mod.Info?.Author) ? "" : mod.Info.Author; return true;
                case "selecteddescription": value = Description(mod); return true;
                case "selectedicon": value = IconPath(mod); return true;
                case "selectedbanner": value = BannerPath(mod); return true;
                case "selectedpath": value = mod.Path; return true;
                case "selectedstate": value = StateText(mod); return true;
                case "selectedwebsite": value = string.IsNullOrWhiteSpace(mod.Info?.Website) ? "No website listed" : mod.Info.Website; return true;
                case "selectedwebsiteenabled": value = string.IsNullOrWhiteSpace(FirstNonEmpty(mod.Info?.UpdateWebsite, mod.Info?.Website)) ? "false" : "true"; return true;
                case "selectedconfigcount": value = (mod.Info?.ConfigFileCount ?? 0).ToString(); return true;
                case "selectedconfiglabel": value = ConfigLabel(mod); return true;
                case "selectedconfigenabled": value = (mod.Info?.ConfigSettings?.Count ?? 0) > 0 ? "true" : "false"; return true;
                case "selectedbodytext": value = VisibleBodyText(); return true;
                case "selectedupdatehint": value = UpdateHint(mod); return true;
                case "selectedupdatetitle": value = UpdateTitle(mod); return true;
                case "selectedupdatecolor": value = UpdateColor(mod); return true;
                case "selectedstatusdetail": value = mod.Protected ? "Required by the manager or game loader." : (mod.Active ? "This mod is active on next startup." : "This mod is disabled until moved back.");
                    return true;
                case "selectedlocation": value = LocationText(mod); return true;
                case "selectedaction": value = ActionText(mod); return true;
                case "selectedactionenabled": value = mod.Protected ? "false" : "true"; return true;
                case "selectedcolor": value = StateColor(mod); return true;
            }

            return false;
        }

        private bool GetRowBinding(ref string value, string bindingName)
        {
            int row;
            string field;
            if (!TryParseRowBinding(bindingName, out row, out field))
            {
                return false;
            }

            List<ManagedMod> filtered = FilteredMods();
            int modIndex = topIndex + row;
            ManagedMod mod = modIndex >= 0 && modIndex < filtered.Count ? filtered[modIndex] : null;
            if (field == "visible") { value = mod != null ? "true" : "false"; return true; }
            if (field == "enabled") { value = mod != null && !mod.Protected ? "true" : "false"; return true; }
            if (mod == null)
            {
                switch (field)
                {
                    case "updatebadgevisible":
                        value = "false";
                        return true;
                    case "updatebadgecolor":
                    case "color":
                    case "subcolor":
                    case "stripecolor":
                        value = "170,170,170,255";
                        return true;
                    case "backcolor":
                        value = "0,0,0,0";
                        return true;
                }

                value = "";
                return true;
            }

            switch (field)
            {
                case "display": value = DisplayName(mod); return true;
                case "folder": value = mod.Name; return true;
                case "meta": value = RowMeta(mod); return true;
                case "updatebadge": value = RowUpdateBadge(mod); return true;
                case "updatebadgevisible": value = RowUpdateBadgeVisible(mod); return true;
                case "updatebadgecolor": value = RowUpdateBadgeColor(mod); return true;
                case "state": value = StateText(mod); return true;
                case "action": value = ActionText(mod); return true;
                case "color": value = StateColor(mod); return true;
                case "subcolor": value = "150,150,150,255"; return true;
                case "stripecolor": value = StateColor(mod); return true;
                case "backcolor": value = modIndex == selectedIndex ? "120,116,91,180" : "0,0,0,105"; return true;
            }

            return false;
        }

        private List<ManagedMod> FilteredMods()
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return mods;
            }

            string query = search.Trim();
            return mods.Where(m =>
                Contains(m.Name, query) ||
                Contains(m.Info?.DisplayName, query) ||
                Contains(m.Info?.Author, query) ||
                Contains(m.Info?.Description, query)).ToList();
        }

        private ManagedMod CurrentSelected()
        {
            List<ManagedMod> filtered = FilteredMods();
            if (selectedIndex < 0 || selectedIndex >= filtered.Count)
            {
                return null;
            }

            return filtered[selectedIndex];
        }

        private void ClampPaging()
        {
            List<ManagedMod> filtered = FilteredMods();
            if (selectedIndex >= filtered.Count)
            {
                selectedIndex = filtered.Count > 0 ? filtered.Count - 1 : 0;
            }

            topIndex = Math.Min(topIndex, MaxTopIndex());
            topIndex = Math.Max(0, topIndex);

            if (selectedIndex < topIndex)
            {
                selectedIndex = topIndex;
            }

            if (selectedIndex >= topIndex + RowCount)
            {
                selectedIndex = Math.Min(filtered.Count - 1, topIndex + RowCount - 1);
            }
        }

        private int PageCount()
        {
            int count = FilteredMods().Count;
            if (count == 0)
            {
                return 0;
            }

            return (count + RowCount - 1) / RowCount;
        }

        private int CurrentPage()
        {
            if (topIndex >= MaxTopIndex())
            {
                int pageCount = PageCount();
                return pageCount == 0 ? 0 : pageCount - 1;
            }

            return topIndex / RowCount;
        }

        private int MaxTopIndex()
        {
            int count = FilteredMods().Count;
            return count <= RowCount ? 0 : count - RowCount;
        }

        private int ScrollHandleHeight()
        {
            int count = FilteredMods().Count;
            if (count <= RowCount)
            {
                return 460;
            }

            return Math.Max(72, 460 * RowCount / count);
        }

        private int ScrollHandlePos()
        {
            int maxTop = MaxTopIndex();
            if (maxTop <= 0)
            {
                return -2;
            }

            int travel = 460 - ScrollHandleHeight();
            return -2 - (travel * topIndex / maxTop);
        }

        private int ConfigScrollHandleHeight()
        {
            int count = CurrentSelected()?.Info?.ConfigSettings?.Count ?? 0;
            if (count <= ConfigRowCount)
            {
                return 480;
            }

            return Math.Max(72, 480 * ConfigRowCount / count);
        }

        private int ConfigScrollHandlePos()
        {
            int maxTop = MaxConfigTopIndex(CurrentSelected());
            if (maxTop <= 0)
            {
                return -2;
            }

            int travel = 480 - ConfigScrollHandleHeight();
            return -2 - (travel * configTopIndex / maxTop);
        }

        private static bool TryParseRowBinding(string bindingName, out int row, out string field)
        {
            row = -1;
            field = "";
            if (bindingName.Length < 5 || !bindingName.StartsWith("mod")) { return false; }

            int pos = 3;
            if (pos >= bindingName.Length || !char.IsDigit(bindingName[pos])) { return false; }

            row = bindingName[pos] - '0';
            field = bindingName.Substring(pos + 1);
            return field.Length > 0;
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DisplayName(ManagedMod mod)
        {
            return string.IsNullOrWhiteSpace(mod.Info?.DisplayName) ? mod.Name : mod.Info.DisplayName;
        }

        private static string Description(ManagedMod mod)
        {
            if (!string.IsNullOrWhiteSpace(mod.Info?.Description))
            {
                return mod.Info.Description;
            }

            return "No description was found in ModInfo.xml.";
        }

        private static string IconPath(ManagedMod mod)
        {
            return ModFolders.ResolveAssetPath(mod, mod?.Info?.Icon, "ModsManager/fallback_icon.png");
        }

        private static string BannerPath(ManagedMod mod)
        {
            return ModFolders.ResolveAssetPath(mod, mod?.Info?.Banner, "ModsManager/fallback_banner.png");
        }

        private static string IconFilePath(ManagedMod mod)
        {
            return ModFolders.ResolveAssetFilePath(mod, mod?.Info?.Icon, "ModsManager/fallback_icon.png");
        }

        private static string BannerFilePath(ManagedMod mod)
        {
            return ModFolders.ResolveAssetFilePath(mod, mod?.Info?.Banner, "ModsManager/fallback_banner.png");
        }

        private static string RowMeta(ManagedMod mod)
        {
            string version = string.IsNullOrWhiteSpace(mod.Info?.Version) ? "" : mod.Info.Version;
            string author = string.IsNullOrWhiteSpace(mod.Info?.Author) ? "" : mod.Info.Author;

            if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(author)) { return version + "  |  " + author; }
            if (!string.IsNullOrEmpty(version)) { return version; }
            if (!string.IsNullOrEmpty(author)) { return author; }
            return LocationText(mod);
        }

        private static string ConfigLabel(ManagedMod mod)
        {
            int count = mod.Info?.ConfigSettings?.Count ?? 0;
            if (count == 0) { return "No editable settings found"; }
            if (count == 1) { return "1 editable setting"; }
            return count + " editable settings";
        }

        private static string ConfigBody(ManagedMod mod)
        {
            List<ConfigSetting> settings = mod.Info?.ConfigSettings ?? new List<ConfigSetting>();
            if (settings.Count == 0)
            {
                return "No editable settings were found.";
            }

            List<string> lines = new List<string>();
            foreach (ConfigSetting setting in settings)
            {
                lines.Add(setting.Label + ": " + setting.DisplayValue);
            }

            return string.Join("\n", lines.ToArray());
        }

        private string VisibleBodyText()
        {
            List<string> lines = CurrentBodyLines();
            int max = Math.Max(0, lines.Count - BodyVisibleRows());
            bodyScrollIndex = Math.Max(0, Math.Min(max, bodyScrollIndex));
            return string.Join("\n", lines.Skip(bodyScrollIndex).Take(BodyVisibleRows()).ToArray());
        }

        private List<string> CurrentBodyLines()
        {
            ManagedMod mod = CurrentSelected();
            if (mod == null)
            {
                return new List<string>();
            }

            string text = Description(mod);
            return WrapLines(text, 74);
        }

        private bool GetConfigBinding(ref string value, string bindingName)
        {
            ManagedMod mod = CurrentSelected();
            if (bindingName == "detailsvisible")
            {
                value = configTabActive ? "false" : "true";
                return true;
            }

            if (bindingName == "configvisible")
            {
                value = configTabActive ? "true" : "false";
                return true;
            }

            if (bindingName == "configtabenabled")
            {
                value = (mod?.Info?.ConfigSettings?.Count ?? 0) > 0 ? "true" : "false";
                return true;
            }

            if (bindingName == "detailstabcolor")
            {
                value = configTabActive ? "0,0,0,245" : "130,0,0,245";
                return true;
            }

            if (bindingName == "configtabcolor")
            {
                value = configTabActive ? "130,0,0,245" : "0,0,0,245";
                return true;
            }

            if (bindingName == "detailstabtextcolor")
            {
                value = "255,255,255,255";
                return true;
            }

            if (bindingName == "configtabtextcolor")
            {
                value = (mod?.Info?.ConfigSettings?.Count ?? 0) > 0 ? "255,255,255,255" : "120,120,120,255";
                return true;
            }

            if (bindingName == "configemptyvisible")
            {
                value = (mod?.Info?.ConfigSettings?.Count ?? 0) == 0 ? "true" : "false";
                return true;
            }

            if (bindingName == "configlistvisible")
            {
                value = (mod?.Info?.ConfigSettings?.Count ?? 0) > 0 ? "true" : "false";
                return true;
            }

            if (bindingName == "bodytextvisible")
            {
                value = configTabActive ? "false" : "true";
                return true;
            }

            if (bindingName == "configeditorvisible")
            {
                value = configTabActive && (mod?.Info?.ConfigSettings?.Count ?? 0) > 0 ? "true" : "false";
                return true;
            }

            if (bindingName == "configsaveenabled")
            {
                value = HasDirtyConfig(mod) ? "true" : "false";
                return true;
            }

            if (bindingName == "configprevenabled")
            {
                value = configTopIndex > 0 ? "true" : "false";
                return true;
            }

            if (bindingName == "confignextenabled")
            {
                value = configTopIndex < MaxConfigTopIndex(mod) ? "true" : "false";
                return true;
            }

            if (bindingName == "configpageinfo")
            {
                int count = mod?.Info?.ConfigSettings?.Count ?? 0;
                value = count <= ConfigRowCount ? "" : ((configTopIndex / ConfigRowCount) + 1) + " / " + ((count + ConfigRowCount - 1) / ConfigRowCount);
                return true;
            }

            if (bindingName == "configscrollhandleheight")
            {
                value = ConfigScrollHandleHeight().ToString();
                return true;
            }

            if (bindingName == "configscrollhandlepos")
            {
                value = ConfigScrollHandlePos().ToString();
                return true;
            }

            int row;
            string field;
            if (!TryParseConfigBinding(bindingName, out row, out field))
            {
                return false;
            }

            ConfigSetting setting = ConfigSettingAt(mod, configTopIndex + row);
            if (field == "visible")
            {
                value = setting != null ? "true" : "false";
                return true;
            }

            if (setting == null)
            {
                value = "";
                return true;
            }

            switch (field)
            {
                case "label": value = setting.Label; return true;
                case "value": value = setting.PendingValue ?? ""; return true;
                case "displayvalue": value = setting.DisplayValue; return true;
                case "description": value = string.IsNullOrWhiteSpace(setting.Description) ? setting.Key : setting.Description; return true;
                case "color": value = ConfigValueColor(setting); return true;
                case "swatchvisible": value = IsHexColor(setting.PendingValue) ? "true" : "false"; return true;
                case "swatchcolor": value = HexToXuiColor(setting.PendingValue); return true;
                case "stripecolor": value = setting.IsDirty ? "255,205,60,255" : "170,170,170,255"; return true;
                case "dirty": value = setting.IsDirty ? "CHANGED" : ""; return true;
                case "dirtycolor": value = setting.IsDirty ? "255,205,60,255" : "0,0,0,0"; return true;
                case "adjustenabled": value = setting.Type.Equals("text", StringComparison.OrdinalIgnoreCase) ? "false" : "true"; return true;
            }

            return false;
        }

        private static ConfigSetting ConfigSettingAt(ManagedMod mod, int row)
        {
            List<ConfigSetting> settings = mod?.Info?.ConfigSettings;
            if (settings == null || row < 0 || row >= settings.Count)
            {
                return null;
            }

            return settings[row];
        }

        private static int MaxConfigTopIndex(ManagedMod mod)
        {
            int count = mod?.Info?.ConfigSettings?.Count ?? 0;
            return count <= ConfigRowCount ? 0 : count - ConfigRowCount;
        }

        private static bool HasDirtyConfig(ManagedMod mod)
        {
            return mod?.Info?.ConfigSettings != null && mod.Info.ConfigSettings.Any(s => s.IsDirty);
        }

        private static string ConfigValueColor(ConfigSetting setting)
        {
            if (IsDefaultConfigValue(setting))
            {
                return "190,120,255,255";
            }

            return setting.IsDirty ? "255,205,60,255" : "245,245,245,255";
        }

        private static bool IsDefaultConfigValue(ConfigSetting setting)
        {
            if (setting == null || string.IsNullOrWhiteSpace(setting.DefaultValue))
            {
                return false;
            }

            string pending = (setting.PendingValue ?? "").Trim();
            string defaultValue = setting.DefaultValue.Trim();
            if (string.Equals(pending, defaultValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            decimal pendingNumber;
            decimal defaultNumber;
            return decimal.TryParse(pending, NumberStyles.Float, CultureInfo.InvariantCulture, out pendingNumber)
                && decimal.TryParse(defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out defaultNumber)
                && pendingNumber == defaultNumber;
        }

        private static bool TryParseConfigBinding(string bindingName, out int row, out string field)
        {
            row = -1;
            field = "";
            if (bindingName.Length < 8 || !bindingName.StartsWith("config")) { return false; }

            int pos = 6;
            if (pos >= bindingName.Length || !char.IsDigit(bindingName[pos])) { return false; }

            row = bindingName[pos] - '0';
            field = bindingName.Substring(pos + 1);
            return field.Length > 0;
        }

        private static bool IsHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
            {
                return false;
            }

            int length = value.Length;
            if (length != 7 && length != 9)
            {
                return false;
            }

            for (int i = 1; i < length; i++)
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

        private static string HexToXuiColor(string value)
        {
            if (!IsHexColor(value))
            {
                return "0,0,0,0";
            }

            int r = Convert.ToInt32(value.Substring(1, 2), 16);
            int g = Convert.ToInt32(value.Substring(3, 2), 16);
            int b = Convert.ToInt32(value.Substring(5, 2), 16);
            int a = value.Length == 9 ? Convert.ToInt32(value.Substring(7, 2), 16) : 255;
            return r + "," + g + "," + b + "," + a;
        }

        private static int BodyVisibleRows()
        {
            return 5;
        }

        private static List<string> WrapLines(string text, int width)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return lines;
            }

            foreach (string rawLine in text.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.TrimEnd();
                while (line.Length > width)
                {
                    int split = line.LastIndexOf(' ', Math.Min(width, line.Length - 1));
                    if (split <= 0) { split = width; }
                    lines.Add(line.Substring(0, split).TrimEnd());
                    line = line.Substring(split).TrimStart();
                }

                lines.Add(line);
            }

            return lines;
        }

        private static string UpdateHint(ManagedMod mod)
        {
            if (mod.Info == null)
            {
                return "";
            }

            switch (mod.Info.UpdateState)
            {
                case UpdateState.UpdateAvailable:
                    return string.IsNullOrWhiteSpace(mod.Info.UpdateMessage) ? "Update available." : mod.Info.UpdateMessage;
                case UpdateState.UpToDate:
                    return "Up to date.";
                case UpdateState.Error:
                    return mod.Info.UpdateMessage;
                case UpdateState.NoManifest:
                    return string.IsNullOrWhiteSpace(mod.Info.Website)
                        ? "No update source listed."
                        : "Website available. No version manifest.";
                case UpdateState.Checking:
                    return "Checking for updates...";
            }

            return "Not checked.";
        }

        private void StartUpdateChecks(bool forceRefresh)
        {
            if (updateCheckRunning)
            {
                return;
            }

            foreach (ManagedMod mod in mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Info?.UpdateManifest))
                {
                    mod.Info.UpdateState = UpdateState.NoManifest;
                    mod.Info.UpdateMessage = "No update manifest listed.";
                }
                else
                {
                    mod.Info.UpdateState = UpdateState.Checking;
                    mod.Info.UpdateMessage = "Checking for updates...";
                }
            }

            updateCheckRunning = true;
            status = restartRequired ? "Restart required. Checking mod updates in the background..." : "Checking mod updates in the background...";
            List<ManagedMod> snapshot = mods.ToList();
            Task.Run(() =>
            {
                try
                {
                    UpdateChecker.CheckAll(snapshot, forceRefresh);
                    int updates = snapshot.Count(m => m.Info?.UpdateState == UpdateState.UpdateAvailable);
                    if (restartRequired)
                    {
                        status = updates == 0
                            ? "Restart required for mod changes to take effect."
                            : "Restart required. " + (updates == 1 ? "1 mod update is available." : updates + " mod updates are available.");
                    }
                    else
                    {
                        status = updates == 0 ? "Update checks complete." : (updates == 1 ? "1 mod update is available." : updates + " mod updates are available.");
                    }
                }
                catch (Exception ex)
                {
                    status = "Update checks failed: " + ex.Message;
                }
                finally
                {
                    updateCheckRunning = false;
                    updateRefreshPending = true;
                }
            });
        }

        private static string LocationText(ManagedMod mod)
        {
            return mod.Active ? "Mods" : ModFolders.DisabledFolderName;
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }

        private static string StateText(ManagedMod mod)
        {
            if (mod.Protected) { return Localization.Get("xuiModManagerProtected"); }
            return Localization.Get(mod.Active ? "xuiModManagerActive" : "xuiModManagerDisabled");
        }

        private static string ActionText(ManagedMod mod)
        {
            if (mod.Protected) { return Localization.Get("xuiModManagerLocked"); }
            return Localization.Get(mod.Active ? "xuiModManagerDisable" : "xuiModManagerEnable");
        }

        private static string StateColor(ManagedMod mod)
        {
            if (mod.Protected) { return "160,160,160,255"; }
            return mod.Active ? "80,220,80,255" : "228,18,21,255";
        }

        private static string UpdateTitle(ManagedMod mod)
        {
            switch (mod.Info?.UpdateState ?? UpdateState.NotChecked)
            {
                case UpdateState.UpdateAvailable: return HasGameVersionUpdate(mod.Info) && !HasModVersionUpdate(mod.Info) ? "GAME UPDATE" : "MOD UPDATE";
                case UpdateState.UpToDate: return "UP TO DATE";
                case UpdateState.Checking: return "CHECKING";
                case UpdateState.Error: return "UPDATE CHECK FAILED";
                case UpdateState.NoManifest: return "NO MANIFEST";
            }

            return "NOT CHECKED";
        }

        private static string UpdateColor(ManagedMod mod)
        {
            switch (mod.Info?.UpdateState ?? UpdateState.NotChecked)
            {
                case UpdateState.UpdateAvailable: return "255,205,60,255";
                case UpdateState.UpToDate: return "95,215,95,255";
                case UpdateState.Error: return "235,85,75,255";
                case UpdateState.Checking: return "120,190,255,255";
            }

            return "170,170,170,255";
        }

        private static string RowUpdateBadge(ManagedMod mod)
        {
            switch (mod.Info?.UpdateState ?? UpdateState.NotChecked)
            {
                case UpdateState.UpdateAvailable: return "UPDATE";
                case UpdateState.Checking: return "...";
                case UpdateState.Error: return "!";
            }

            return "";
        }

        private static string RowUpdateBadgeVisible(ManagedMod mod)
        {
            UpdateState state = mod.Info?.UpdateState ?? UpdateState.NotChecked;
            return state == UpdateState.UpdateAvailable || state == UpdateState.Checking || state == UpdateState.Error ? "true" : "false";
        }

        private static string RowUpdateBadgeColor(ManagedMod mod)
        {
            switch (mod.Info?.UpdateState ?? UpdateState.NotChecked)
            {
                case UpdateState.UpdateAvailable: return "255,190,45,245";
                case UpdateState.Error: return "220,55,45,245";
                case UpdateState.Checking: return "70,130,190,220";
            }

            return "0,0,0,0";
        }

        private static string VersionLine(ManagedMod mod)
        {
            if (mod.Info == null || string.IsNullOrWhiteSpace(mod.Info.Version))
            {
                return "";
            }

            if (HasModVersionUpdate(mod.Info))
            {
                return "Version " + mod.Info.Version + " -> " + mod.Info.LatestVersion;
            }

            return "Version " + mod.Info.Version;
        }

        private static string GameVersionLine(ManagedMod mod)
        {
            if (mod.Info == null)
            {
                return "";
            }

            string current = mod.Info.GameVersion;
            if (string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(mod.Info.LatestGameVersion))
            {
                return "";
            }

            if (HasGameVersionUpdate(mod.Info))
            {
                return string.IsNullOrWhiteSpace(current)
                    ? "Game " + mod.Info.LatestGameVersion
                    : "Game " + current + " -> " + mod.Info.LatestGameVersion;
            }

            return string.IsNullOrWhiteSpace(current) ? "" : "Game " + current;
        }

        private static bool HasModVersionUpdate(ModInfo info)
        {
            return info != null
                && info.UpdateState == UpdateState.UpdateAvailable
                && !string.IsNullOrWhiteSpace(info.LatestVersion)
                && UpdateChecker.CompareVersions(info.Version, info.LatestVersion) < 0;
        }

        private static bool HasGameVersionUpdate(ModInfo info)
        {
            return info != null
                && info.UpdateState == UpdateState.UpdateAvailable
                && !string.IsNullOrWhiteSpace(info.LatestGameVersion)
                && UpdateChecker.CompareVersions(info.GameVersion, info.LatestGameVersion) < 0;
        }
    }
}





