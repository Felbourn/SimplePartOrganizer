//===================================================================================================================================================
//  Simple Part Organizer
//===================================================================================================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.IO;

//---------------------------------------------------------------------------------------------------------------------------------------------------
namespace PartOrganizer
{
    // this attribute ensures the class loads in the editor
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    class PartOrganizer : MonoBehaviour
    {
        //---------------------------------------------------------------------------------------------------------------------------------------
        public enum LogLevel
        {
            ERROR,      
            WARN,
            VERSION,    // Release mode
            INFO,
            VERBOSE,    // Debug mode
        };
        //static private LogLevel logLevel = LogLevel.VERBOSE; // Debug
        static private LogLevel logLevel = LogLevel.VERSION; // Release

        //---------------------------------------------------------------------------------------------------------------------------------------
        static public void Log(LogLevel level, string message)
        {
            if (level > logLevel)
                return;
            message = "[BOB] " + message;
            switch (level)
            {
                case LogLevel.ERROR: Debug.LogError(message); break;
                case LogLevel.WARN:  Debug.LogWarning(message); break;
                default:             Debug.Log(message); break;
            }
        }

        //---------------------------------------------------------------------------------------------------------------------------------------
        public class PartInfo
        {
            //-------------------------------------------------------------------------------------------------------------------------------
            public PartInfo(AvailablePart part)
            {
                // if the attach points have different sizes then it's probably an adapter and we'll place
                // it half way between the smallest and largest attach point of the things it connects
                if (part.partPrefab.attachNodes == null)
                {
                    Log(LogLevel.INFO, string.Format("{0} has no attach points", part.name));
                    partSize = "No Size";
                }
                else if (part.partPrefab.attachNodes.Count < 0)
                {
                    Log(LogLevel.INFO, string.Format("{0} has negative attach points", part.name));
                    partSize = "No Size";
                }
                else if (part.partPrefab.attachNodes.Count < 1)
                {
                    Log(LogLevel.INFO, string.Format("{0} has no attachNodes", part.name));
                    partSize = "No Size";
                }
                else
                {
                    double small = 99999;
                    double large = 0;
                    foreach (var attach in part.partPrefab.attachNodes)
                    {
                        small = Math.Min(small, attach.size);
                        large = Math.Max(large, attach.size);
                    }
                    sortSize = (small + large) / 2;
                    if (small == large)
                        partSize = Math.Round(small, 2).ToString("0.00");
                    else
                        partSize = Math.Round(small, 2).ToString("0.00") + " to " + Math.Round(large, 2).ToString("0.00");
                    Log(LogLevel.INFO, string.Format("{0} is sortSize {1} partSize {2}", part.name, sortSize, partSize));
                }
            }

            //-------------------------------------------------------------------------------------------------------------------------------
            public string partSize;
            public double sortSize;

            public int defaultPos;
            public int alphabeticalPos;
            public int dryMassPos;
            public int wetMassPos;
            public int manufacturerPos;
            public int sizePos;
        };

        //---------------------------------------------------------------------------------------------------------------------------------------
        enum MouseStatus
        {
            OFF_UI,
            IN_SORT_WINDOW,
            IN_FILTER_WINDOW,
            IN_FILTER_CHILD_MOD,
            IN_FILTER_CHILD_SIZE,
            IN_FILTER_CHILD_MODULES,
        };
        private MouseStatus mouseStatus;

        //---------------------------------------------------------------------------------------------------------------------------------------
        private const int MODULE_WINDOW_ID = 94;
        private const int SIZE_WINDOW_ID   = 95;
        private const int MOD_WINDOW_ID    = 96;
        private const int FILTER_WINDOW_ID = 97;
        private const int SORT_WINDOW_ID   = 98;

        private struct ToggleState
        {
            public bool enabled;
            public bool latched;
        }

        private DateTime delayedClose;

        private Rect sortWindowRect = new Rect(EditorPanels.Instance.partsPanelWidth + 20, Screen.height - 25 - 280, 150, 280);
        private Rect filterWindowRect = new Rect(EditorPanels.Instance.partsPanelWidth + 90, Screen.height - 25 - 280, 150, 280);

        private Rect filterChildWindowRect;
        private Rect modWindowRect;
        private Rect modWindowViewRect;
        private Rect sizeWindowRect;
        private Rect modulesWindowRect;
        private Rect modulesWindowViewRect;

        private Vector2 modWindowScrollPosition = new Vector2();
        private Vector2 modulesWindowScrollPosition = new Vector2();

        private Dictionary<string,ToggleState> modButtons  = new Dictionary<string,ToggleState>();
        private Dictionary<string,ToggleState> sizeButtons = new Dictionary<string,ToggleState>();
        private Dictionary<string,ToggleState> moduleButtons = new Dictionary<string,ToggleState>();

        private UrlDir.UrlConfig[] configs = GameDatabase.Instance.GetConfigs("PART");

        private Dictionary<AvailablePart, PartInfo> partInfos = new Dictionary<AvailablePart, PartInfo>();

        private Dictionary<string, HashSet<AvailablePart>> modHash = new Dictionary<string, HashSet<AvailablePart>>();
        private Dictionary<string, HashSet<AvailablePart>> sizeHash = new Dictionary<string, HashSet<AvailablePart>>();
        private Dictionary<string, HashSet<AvailablePart>> moduleHash = new Dictionary<string, HashSet<AvailablePart>>();

        private string defaultSortButton;
        private string nameSortButton;
        private string drySortButton;
        private string wetSortButton;
        private string makerSortButton;
        private string sizeSortButton;


        //-------------------------------------------------------------------------------------------------------------------------------------------
        // KSP API
        //-------------------------------------------------------------------------------------------------------------------------------------------
        public void Start()
        {
            Log(LogLevel.VERSION, "SimplePartOrganizer - Version 1.2.1 - by Bob Fitch, aka Felbourn");
            Log(LogLevel.VERSION, "Tested with Kerbal Space Program 0.23.5");

            Log(LogLevel.INFO, "ENTER: Start");

            SetDefaultButtonNames();
            List<AvailablePart> loadedParts = GetPartsList();
            InitialPartsScan(loadedParts);
            CreateSortingSequences(loadedParts);
            LoadValuesFromConfig();
            DefineFilters();
            DefineFilterWindows();

            Log(LogLevel.INFO, "LEAVE: Start");
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        public void OnDestroy()
        {
            // does nothing anymore since I save the config when a change is made
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        public void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
                GUI.skin = HighLogic.Skin;
         
            // we only want the sort & filter buttons if the parts panel is showing
            if (EditorLogic.fetch.editorScreen != EditorLogic.EditorScreen.Parts)
                return;

            if (GUI.Button(new Rect(EditorPanels.Instance.partsPanelWidth + 20, Screen.height - 20, 60, 20), "Sort"))
            {
                Log(LogLevel.INFO, string.Format("click for IN_SORT_WINDOW"));
                mouseStatus = MouseStatus.IN_SORT_WINDOW;
                delayedClose = DateTime.Now;
            }             
            else if (GUI.Button(new Rect(EditorPanels.Instance.partsPanelWidth + 90, Screen.height - 20, 60, 20), "Filter"))
            {
                Log(LogLevel.INFO, string.Format("click for IN_FILTER_WINDOW"));
                mouseStatus = MouseStatus.IN_FILTER_WINDOW;
                delayedClose = DateTime.Now;
            }

            Vector2 mp = Event.current.mousePosition;

            if (mouseStatus == MouseStatus.IN_SORT_WINDOW)
            {
                GUI.Window(SORT_WINDOW_ID, sortWindowRect, SortWindowHandler, "Sort By");
                if (sortWindowRect.Contains(mp))
                    delayedClose = DateTime.Now;
            }
            else if (mouseStatus >= MouseStatus.IN_FILTER_WINDOW)
            {
                GUI.Window(FILTER_WINDOW_ID, filterWindowRect, FilterWindowHandler, "Filter");
                if (filterWindowRect.Contains(mp))
                    delayedClose = DateTime.Now;
            }

            if (mouseStatus == MouseStatus.IN_FILTER_CHILD_MOD)
            {
                modWindowScrollPosition = GUI.BeginScrollView(modWindowRect, modWindowScrollPosition, modWindowViewRect);
                filterChildWindowRect = modWindowRect;
                if (modWindowRect.Contains(mp))
                    delayedClose = DateTime.Now;
                GUIStyle labelStyle = new GUIStyle(HighLogic.Skin.label);
                labelStyle.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(0, 0, 130, 30), "Mod Filter", labelStyle);
                FilterChildWindowHandler(MOD_WINDOW_ID);
                GUI.EndScrollView();
            }
            else if (mouseStatus == MouseStatus.IN_FILTER_CHILD_SIZE)
            {
                filterChildWindowRect = sizeWindowRect;
                if (sizeWindowRect.Contains(mp))
                    delayedClose = DateTime.Now;
                GUI.Window(SIZE_WINDOW_ID, filterChildWindowRect, FilterChildWindowHandler, "Size Filter");
            }
            else if (mouseStatus == MouseStatus.IN_FILTER_CHILD_MODULES)
            {
                modulesWindowScrollPosition = GUI.BeginScrollView(modulesWindowRect, modulesWindowScrollPosition, modulesWindowViewRect);
                filterChildWindowRect = modulesWindowRect;
                if (modulesWindowRect.Contains(mp))
                    delayedClose = DateTime.Now;
                GUIStyle labelStyle = new GUIStyle(HighLogic.Skin.label);
                labelStyle.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(0, 0, 130, 30), "Module Filter", labelStyle);
                FilterChildWindowHandler(MODULE_WINDOW_ID);
                GUI.EndScrollView();
            }

            if (mouseStatus != MouseStatus.OFF_UI)
            {
                TimeSpan elapsed = DateTime.Now - delayedClose;
                if (elapsed.TotalSeconds > 1)
                {
                    Log(LogLevel.INFO, string.Format("delay expired, change to OFF_UI"));
                    mouseStatus = MouseStatus.OFF_UI;
                }
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        List<AvailablePart> GetPartsList()
        {
            List<AvailablePart> loadedParts = new List<AvailablePart>();
            loadedParts.AddRange(PartLoader.LoadedPartsList); // make a copy we can manipulate

            // these two parts are internal and just serve to mess up our lists and stuff
            AvailablePart kerbalEVA = null;
            AvailablePart flag = null;
            foreach (var part in loadedParts)
            {
                if (part.name == "kerbalEVA")
                    kerbalEVA = part;
                else if (part.name == "flag")
                    flag = part;
            }

            // still need to prevent errors with null refs when looking up these parts though
            if (kerbalEVA != null)
            {
                loadedParts.Remove(kerbalEVA);
                partInfos.Add(kerbalEVA, new PartInfo(kerbalEVA));
            }
            if (flag != null)
            {
                loadedParts.Remove(flag);
                partInfos.Add(flag, new PartInfo(flag));
            }
            return loadedParts;
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void InitialPartsScan(List<AvailablePart> loadedParts)
        {
            int index = 1;
            foreach (var part in loadedParts)
            {
                Log(LogLevel.INFO, string.Format("PROCESS {0}", part.name));

                PartInfo partInfo = new PartInfo(part);
                partInfos.Add(part, partInfo);

                partInfo.defaultPos = index++;

                // add the size to the list of all sizes known if it's the first time we've seen this part size
                if (!sizeButtons.ContainsKey(partInfo.partSize))
                {
                    Log(LogLevel.VERBOSE, string.Format("define new size filter key {0}", partInfo.partSize));
                    sizeButtons.Add(partInfo.partSize, new ToggleState() { enabled = false, latched = false });
                    sizeHash.Add(partInfo.partSize, new HashSet<AvailablePart>());
                }
                Log(LogLevel.VERBOSE, string.Format("add {0} to sizeHash for {1}", part.name, partInfo.partSize));
                sizeHash[partInfo.partSize].Add(part);

                // the part's base directory name is used to filter entire mods in and out
                string partModName = FindPartMod(part);
                if (!modButtons.ContainsKey(partModName))
                {
                    Log(LogLevel.VERBOSE, string.Format("define new mod filter key {0}", partModName));
                    modButtons.Add(partModName, new ToggleState() { enabled = false, latched = false });
                    modHash.Add(partModName, new HashSet<AvailablePart>());
                }
                Log(LogLevel.VERBOSE, string.Format("add {0} to modHash for {1}", part.name, partModName));
                modHash[partModName].Add(part);

                // save all the module names that are anywhere in this part
                if (part.partPrefab == null)
                    continue;
                if (part.partPrefab.Modules == null)
                    continue;

                foreach (PartModule module in part.partPrefab.Modules)
                {
                    string fullName = module.moduleName;
                    if (fullName == null)
                    {
                        Log(LogLevel.VERBOSE, string.Format("{0} has a null moduleName, skipping it", part.name));
                        continue;
                    }
                    Log(LogLevel.VERBOSE, string.Format("scan part '{0}' module [{2}]'{1}'", part.name, fullName, fullName.Length));
                    string moduleName = UsefulModuleName(fullName);
                    if (!moduleButtons.ContainsKey(moduleName))
                    {
                        Log(LogLevel.VERBOSE, string.Format("define new module filter key {0}", moduleName));
                        moduleButtons.Add(moduleName, new ToggleState() { enabled = false, latched = false });
                        moduleHash.Add(moduleName, new HashSet<AvailablePart>());
                    }
                    Log(LogLevel.VERBOSE, string.Format("add {0} to moduleHash for {1}", part.name, moduleName));
                    moduleHash[moduleName].Add(part);
                }
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void CreateSortingSequences(List<AvailablePart> loadedParts)
        {
            int index;

            // create the sort order for alphabetical
            //Log(LogLevel.VERBOSE, "ALPHABETICAL");
            loadedParts.Sort((part1, part2) =>
                String.Compare(part1.title, part2.title)
            );
            index = 1;
            foreach (var part in loadedParts)
            {
                //Log(LogLevel.VERBOSE, string.Format("{0}: {1} is {2}", index, part.name, part.title));
                partInfos[part].alphabeticalPos = index++;
            }

            // create the sort order for dry mass
            //Log(LogLevel.VERBOSE, "DRY MASS");
            loadedParts.Sort((part1, part2) =>
                (int)(1000000 * (part1.partPrefab.mass - part2.partPrefab.mass)) + partInfos[part1].defaultPos - partInfos[part2].defaultPos
            );
            index = 1;
            foreach (var part in loadedParts)
            {
                //Log(LogLevel.VERBOSE, string.Format("{0}: {1} = {2} sorted with {3}", index, part.name, part.partPrefab.mass, (int)(1000000 * part.partPrefab.mass + partInfos[part].defaultPos)));
                partInfos[part].dryMassPos = index++;
            }

            // create the sort order for manufacturer
            //Log(LogLevel.VERBOSE, "MANUFACTURER");
            loadedParts.Sort((part1, part2) =>
                1000000 * String.Compare(part1.manufacturer, part2.manufacturer) + partInfos[part1].defaultPos - partInfos[part2].defaultPos
            );
            index = 1;
            foreach (var part in loadedParts)
            {
                //Log(LogLevel.VERBOSE, string.Format("{0}: {1} from {2}", index, part.name, part.manufacturer));
                partInfos[part].manufacturerPos = index++;
            }

            // create the sort order for size
            //Log(LogLevel.VERBOSE, "SIZE");
            loadedParts.Sort((part1, part2) =>
                (int)(1000000 * (partInfos[part1].sortSize - partInfos[part2].sortSize)) + partInfos[part1].defaultPos - partInfos[part2].defaultPos
            );
            index = 1;
            foreach (var part in loadedParts)
            {
                //Log(LogLevel.VERBOSE, string.Format("{0}: {1} = {2}", index, part.name, partInfos[part].sortSize));
                partInfos[part].sizePos = index++;
            }

            // create the sort order for wet mass, but remove internal parts while sorting to prevent null-refs
            //Log(LogLevel.VERBOSE, "WET MASS");
            loadedParts.Sort((part1, part2) =>
                (int)(1000000 * (part1.partPrefab.mass + part1.partPrefab.GetResourceMass() - part2.partPrefab.mass - part2.partPrefab.GetResourceMass())) + partInfos[part1].defaultPos - partInfos[part2].defaultPos
            );
            index = 1;
            foreach (var part in loadedParts)
            {
                //Log(LogLevel.VERBOSE, string.Format("{0}: {1} = {2}", index, part.name, part.partPrefab.mass + part.partPrefab.GetResourceMass()));
                partInfos[part].wetMassPos = index++;
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void DefineFilterWindows()
        {
            modWindowRect = FilterWindowRect("Mods", modButtons.Count);
            sizeWindowRect = FilterWindowRect("Sizes", sizeButtons.Count);
            modulesWindowRect = FilterWindowRect("Modules", moduleButtons.Count);

            modWindowViewRect = new Rect(0, 0, modWindowRect.width, 40 * (modButtons.Count + 3));
            modulesWindowViewRect = new Rect(0, 0, modulesWindowRect.width, 40 * (moduleButtons.Count + 3));

            filterChildWindowRect = modWindowRect;
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        Rect FilterWindowRect(string name, int buttons)
        {
            float height = 40 * (buttons + 3);
            float top = filterWindowRect.yMin + Math.Min(0, 280 - height);
            Rect answer = new Rect(filterWindowRect.xMax, top, filterWindowRect.width + 150, height);
            if (answer.yMin < 100)
                answer.yMin = 100;
            //Log(LogLevel.INFO, string.Format("defining window {4} at ({0},{1},{2},{3})", answer.xMin, answer.yMin, answer.width, answer.height, name));
            return answer;
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        string UsefulModuleName(string longName)
        {
            if (longName.StartsWith("Module"))
                return longName.Substring(6);
            if (longName.StartsWith("FXModule"))
                return "FX" + longName.Substring(8);
            return longName;
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        private void LoadValuesFromConfig()
        {
            Log(LogLevel.INFO, "LoadValuesFromConfig");

            PluginConfiguration config = PluginConfiguration.CreateForType<PartOrganizer>();
            config.load();
            LoadConfigSection(config, "Module", moduleButtons);
            LoadConfigSection(config, "Mod", modButtons);
            LoadConfigSection(config, "Size", sizeButtons);

            string sorting = config.GetValue<string>("Sorting");
            if (!String.IsNullOrEmpty(sorting))
                RunSort(sorting);
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void LoadConfigSection(PluginConfiguration config, string prefix, Dictionary<string, ToggleState> buttons)
        {
            Log(LogLevel.INFO, string.Format("LoadConfigSection {0}", prefix));
            for (int i = 0; ; i++)
            {
                string sectionName = prefix + i.ToString();
                string entryName = config.GetValue<string>(sectionName);
                if (String.IsNullOrEmpty(entryName))
                    return;
                if (!buttons.ContainsKey(entryName))
                    continue;

                ToggleState s = buttons[entryName];
                s.enabled = true;
                buttons[entryName] = s;
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void DefineFilters()
        {
            EditorPartList.Instance.ExcludeFilters.AddFilter(new EditorPartListFilter("Mod Filter", (part => !PartInFilteredButtons(part, modButtons, modHash))));
            EditorPartList.Instance.ExcludeFilters.AddFilter(new EditorPartListFilter("Size Filter", (part => !PartInFilteredButtons(part, sizeButtons, sizeHash))));
            EditorPartList.Instance.ExcludeFilters.AddFilter(new EditorPartListFilter("Modules Filter", (part => !PartInFilteredButtons(part, moduleButtons, moduleHash))));
            EditorPartList.Instance.Refresh();
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        private void FilterChildWindowHandler(int id)
        {
            Dictionary<string, ToggleState> states;
            
            switch (id)
            {
                case MODULE_WINDOW_ID: states = moduleButtons; break;
                case SIZE_WINDOW_ID:   states = sizeButtons;   break;
                case MOD_WINDOW_ID:    states = modButtons;    break;
                default: return;
            }

            if (GUI.Button(new Rect(10, 30, 130, 30), "Select All"))
            {
                var names = new List<string>(states.Keys);
                foreach (string name in names)
                {
                    ToggleState state = states[name];
                    state.enabled = true;
                    states[name] = state;
                }
                SaveConfig();
            }

            if (GUI.Button(new Rect(10, 70, 130, 30), "De-Select All"))
            {
                var names = new List<string>(states.Keys);
                foreach (string name in names)
                {
                    ToggleState state = states[name];
                    state.enabled = false;
                    states[name] = state;
                }
                SaveConfig();
            }

            float top = 120f;
            var keys = new List<string>(states.Keys);
            keys.Sort();
            foreach (string name in keys)
            {
                ToggleState state = states[name];
                string truncatedName = (name.Length > 32)? name.Remove(31) : name;
                bool before = state.enabled;
                state.enabled = GUI.Toggle(new Rect(10, top, 250, 30), state.enabled, truncatedName, HighLogic.Skin.button);
                if (before != state.enabled)
                    SaveConfig();
                top += 40;

                if (state.enabled && !state.latched)
                {
                    state.latched = true;
                    states[name] = state;                    
                    EditorPartList.Instance.Refresh();
                }
                else if (!state.enabled && state.latched)
                {
                    state.latched = false;
                    states[name] = state;
                    EditorPartList.Instance.Refresh();
                }
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void SaveConfig(string sorting = null)
        {
            Log(LogLevel.INFO, "save config");
            PluginConfiguration config = PluginConfiguration.CreateForType<PartOrganizer>();

            if (sorting != null)
                config.SetValue("Sorting", sorting);

            int i = 0;
            foreach (var mod in modButtons)
            {
                if (mod.Value.enabled)
                    config.SetValue("Mod" + (i++).ToString(), mod.Key);
            }
            i = 0;
            foreach (var size in sizeButtons)
            {
                if (size.Value.enabled)
                    config.SetValue("Size" + (i++).ToString(), size.Key);
            }
            i = 0;
            foreach (var module in moduleButtons)
            {
                if (module.Value.enabled)
                    config.SetValue("Module" + (i++).ToString(), module.Key);
            }
            config.save();
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        bool PartInFilteredButtons(AvailablePart part, Dictionary<string, ToggleState> buttons, Dictionary<string, HashSet<AvailablePart>> filterHash)
        {
            foreach (string name in buttons.Keys)
            {
                if (!buttons[name].enabled)
                    continue;
                if (filterHash[name].Contains(part))
                    return true;
            }
            return false;
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        string FindPartMod(AvailablePart part)
        {
            UrlDir.UrlConfig config = Array.Find<UrlDir.UrlConfig>(configs, (c => (part.name == c.name.Replace('_', '.'))));
            if (config == null)
                return "";
            var id = new UrlDir.UrlIdentifier(config.url);
            return id[0];
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void RunSort(string sorting)
        {
            if (sorting == "none")
                return;

            SetDefaultButtonNames();

            // parts in the editor panels are drawn from the LoadedPartsList in order, so re-ordering the list re-orders the parts in the panel
            if (sorting == "default")
            {
                PartLoader.LoadedPartsList.Sort((part1, part2) => (partInfos[part1].defaultPos - partInfos[part2].defaultPos));
                defaultSortButton = SelectButton(defaultSortButton);
            }
            else if (sorting == "name")
            {
                PartLoader.LoadedPartsList.Sort((part1, part2) => (partInfos[part1].alphabeticalPos - partInfos[part2].alphabeticalPos));
                nameSortButton = SelectButton(nameSortButton);
            }
            else if (sorting == "dry")
            {
                PartLoader.LoadedPartsList.Sort((part1, part2) => (partInfos[part1].dryMassPos - partInfos[part2].dryMassPos));
                drySortButton = SelectButton(drySortButton);
            }
            else if (sorting == "wet")
            {
                PartLoader.LoadedPartsList.Sort((part1, part2) => (partInfos[part1].wetMassPos - partInfos[part2].wetMassPos));
                wetSortButton = SelectButton(wetSortButton);
            }
            else if (sorting == "maker")
            {
                PartLoader.LoadedPartsList.Sort((part1, part2) => (partInfos[part1].manufacturerPos - partInfos[part2].manufacturerPos));
                makerSortButton = SelectButton(makerSortButton);
            }
            else if (sorting == "size")
            {
                PartLoader.LoadedPartsList.Sort((part1, part2) => (partInfos[part1].sizePos - partInfos[part2].sizePos));
                sizeSortButton = SelectButton(sizeSortButton);
            }

            EditorPartList.Instance.Refresh();
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        void SetDefaultButtonNames()
        {
            defaultSortButton = "Default";
            nameSortButton = "Name";
            drySortButton = "Dry Mass";
            wetSortButton = "Wet Mass";
            makerSortButton = "Manufacturer";
            sizeSortButton = "Size";
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        string SelectButton(string name)
        {
            return "> " + name + " <";
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        private void SortWindowHandler(int windowID)
        {
            string sorting = "none";
            if (GUI.Button(new Rect(10, 30, 130, 30), defaultSortButton))
                sorting = "default";
            else if (GUI.Button(new Rect(10, 80, 130, 30), nameSortButton))
                sorting = "name";
            else if (GUI.Button(new Rect(10, 120, 130, 30), drySortButton))
                sorting = "dry";
            else if (GUI.Button(new Rect(10, 160, 130, 30), wetSortButton))
                sorting = "wet";
            else if (GUI.Button(new Rect(10, 200, 130, 30), makerSortButton))
                sorting = "maker";
            else if (GUI.Button(new Rect(10, 240, 130, 30), sizeSortButton))
                sorting = "size";

            if (sorting == "none")
                return;

            RunSort(sorting);
            SaveConfig(sorting);
        }

        //-------------------------------------------------------------------------------------------------------------------------------------------
        private void FilterWindowHandler(int windowID)
        {
            if (GUI.Button(new Rect(10, 30, 130, 30), "Mod->"))
                mouseStatus = MouseStatus.IN_FILTER_CHILD_MOD;
            else if (GUI.Button(new Rect(10, 70, 130, 30), "Size->"))
                mouseStatus = MouseStatus.IN_FILTER_CHILD_SIZE;
            else if (GUI.Button(new Rect(10, 110, 130, 30), "Modules->"))
                mouseStatus = MouseStatus.IN_FILTER_CHILD_MODULES;
        }
    }
}
