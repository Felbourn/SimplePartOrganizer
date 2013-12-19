using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.IO;

namespace PartOrganizer
{
    // this attribute ensures the class loads in the editor
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    class PartOrganizer : MonoBehaviour
    {
        private const int SORT_WINDOW_ID = 98;
        private const int FILTER_WINDOW_ID = 97;
        private const int MOD_WINDOW_ID = 96;
        private const int SIZE_WINDOW_ID = 95;
        private const int TYPE_WINDOW_ID = 94;

        private struct ToggleState
        {
            public bool enabled;
            public bool latched;
        }

        private bool sortWindowVisible = false;
        private bool mouseEnteredSortWindow = false;
        private bool filterWindowVisible = false;
        private bool filterChildWindowVisible = false;
        private bool mouseEnteredFilterWindow = false;
        private bool mouseEnteredFilterChildWindow = false;
        private Rect sortWindowRect, filterWindowRect, filterChildWindowRect, modWindowRect,
            modWindowViewRect, sizeWindowRect, typeWindowRect;
        private Vector2 modWindowScrollPosition = new Vector2();
        private Dictionary<string, ToggleState> mods = new Dictionary<string, ToggleState>();
        private Dictionary<string, ToggleState> sizes = new Dictionary<string, ToggleState>();
        private Dictionary<string, ToggleState> types = new Dictionary<string, ToggleState>();

        private UrlDir.UrlConfig[] configs = GameDatabase.Instance.GetConfigs("PART");
        private string filterChildType = "Mod";
        private List<AvailablePart> defaultPartSort = new List<AvailablePart>();

        private string typeSelected = "";

        public void Start()
        {       
            // store default sort order (AddRange creates a copy instead of a reference)
            defaultPartSort.AddRange(PartLoader.LoadedPartsList);

            // fill mods
            foreach (var c in configs)
            {
                var id = new UrlDir.UrlIdentifier(c.url);
                if (!mods.ContainsKey(id[0]))
                    mods.Add(id[0], new ToggleState() { enabled = false, latched = false });
            }

            // fill sizes
            foreach (var part in PartLoader.LoadedPartsList)
            {
                string partSize = Math.Round(getPartSize(part), 2).ToString();
                if (!sizes.ContainsKey(partSize))
                {
                    sizes.Add(partSize, new ToggleState() { enabled = false, latched = false });
                }
            }

            // fill types
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleDecouple"))))
                types.Add("Decoupler", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleEngine"))))
                types.Add("Engine", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Resources.Contains("LiquidFuel"))))
                types.Add("Fuel: Liquid", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Resources.Contains("MonoPropellant"))))
                types.Add("Fuel: RCS", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleRCS"))))
                types.Add("RCS", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleDeployableSolarPanel"))))
                types.Add("Solar", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleDockingNode"))))
                types.Add("Docking", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.GetType().ToString() == "HLandingLeg")))
                types.Add("Landing Gear", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleParachute"))))
                types.Add("Parachute", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleWheel"))))
                types.Add("Wheel", new ToggleState() { enabled = false, latched = false });
            if (PartLoader.LoadedPartsList.Exists(part => (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("RetractableLadder"))))
                types.Add("Ladder", new ToggleState() { enabled = false, latched = false });

            loadValuesFromConfig();

            EditorPartListFilter modfilter = new EditorPartListFilter("Mod Filter",
                        (part => !partInFilteredMods(part)));
            EditorPartList.Instance.ExcludeFilters.AddFilter(modfilter);
            EditorPartListFilter sizefilter = new EditorPartListFilter("Size Filter",
                        (part => !partInFilteredSizes(part)));
            EditorPartList.Instance.ExcludeFilters.AddFilter(sizefilter);
            EditorPartListFilter typefilter = new EditorPartListFilter("Type Filter",
                        (part => partInFilteredTypes(part)));
            EditorPartList.Instance.ExcludeFilters.AddFilter(typefilter);
            EditorPartList.Instance.Refresh();

            sortWindowRect = new Rect(EditorPanels.Instance.partsPanelWidth + 20, Screen.height - 25 - 280, 150, 280);
            filterWindowRect = new Rect(EditorPanels.Instance.partsPanelWidth + 90, Screen.height - 25 - 230, 150, 230);
            modWindowRect = new Rect(filterWindowRect.xMax, filterWindowRect.yMin + Math.Min(0, 230 - (40 * mods.Count + 120)),
                filterWindowRect.width, 40 * mods.Count + 120);
            modWindowViewRect = new Rect(0, 0, modWindowRect.width, modWindowRect.height);
            if (modWindowRect.yMin < 100)
            {
                modWindowRect.yMin = 100;
                modWindowRect.width = 170;
            }
            sizeWindowRect = new Rect(filterWindowRect.xMax, filterWindowRect.yMin + Math.Min(0, 230 - (40 * sizes.Count + 120)),
                filterWindowRect.width, 40 * sizes.Count + 120);
            typeWindowRect = new Rect(filterWindowRect.xMax, filterWindowRect.yMin + Math.Min(0, 230 - (40 * types.Count + 30)),
                filterWindowRect.width, 40 * types.Count + 30);
            filterChildWindowRect = modWindowRect;
        }

        private void loadValuesFromConfig()
        {
            int i = 0;
            PluginConfiguration config = PluginConfiguration.CreateForType<PartOrganizer>();

            config.load();
            if (!String.IsNullOrEmpty(config.GetValue<string>("Type Selected")))
            {
                typeSelected = config.GetValue<string>("Type Selected");
                ToggleState s = types[typeSelected];
                s.enabled = true;
                types[typeSelected] = s;
            }
            while (!String.IsNullOrEmpty(config.GetValue<string>("Mod" + i.ToString())))
            {
                string mod = config.GetValue<string>("Mod" + i.ToString());
                if (mods.ContainsKey(mod))
                {
                    ToggleState s = mods[mod];
                    s.enabled = true;
                    mods[mod] = s;
                }
                i++;
            }
            i = 0;
            while (!String.IsNullOrEmpty(config.GetValue<string>("Size" + i.ToString())))
            {
                string size = config.GetValue<string>("Size" + i.ToString());
                Debug.Log(size);
                if (sizes.ContainsKey(size))
                {
                    ToggleState s = sizes[size];
                    s.enabled = true;
                    sizes[size] = s;
                }
                i++;
            }
        }

        public void OnDestroy()
        {
            PluginConfiguration config = PluginConfiguration.CreateForType<PartOrganizer>();

            int i = 0;
            foreach (var mod in mods)
            {
                if (mod.Value.enabled)
                    config.SetValue("Mod" + (i++).ToString(), mod.Key);
            }
            i = 0;
            foreach (var size in sizes)
            {
                if (size.Value.enabled)
                    config.SetValue("Size" + (i++).ToString(), size.Key);
            }
            config.SetValue("Type Selected", typeSelected);
            config.save();
        }
        
        public void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.skin = HighLogic.Skin;
            }
         
            // we only want the sort/filter buttons if the parts panel is showing
            if (EditorLogic.fetch.editorScreen == EditorLogic.EditorScreen.Parts)
            {
                if (GUI.Button(new Rect(EditorPanels.Instance.partsPanelWidth + 20, Screen.height - 20, 60, 20), "Sort"))
                {
                    sortWindowVisible = true;
                    filterWindowVisible = false;
                }

                if (GUI.Button(new Rect(EditorPanels.Instance.partsPanelWidth + 90, Screen.height - 20, 60, 20), "Filter"))
                {
                    sortWindowVisible = false;
                    filterWindowVisible = true;
                }

                Vector2 mp = Event.current.mousePosition;

                // this code makes the windows visible until you mouse away from them, like a tooltip window
                if (sortWindowVisible)
                {
                    GUI.Window(SORT_WINDOW_ID, sortWindowRect, sortWindowHandler, "Sort By");

                    if (!mouseEnteredSortWindow && sortWindowRect.Contains(mp))
                        mouseEnteredSortWindow = true;
                    else if (mouseEnteredSortWindow && !sortWindowRect.Contains(mp))
                    {
                        mouseEnteredSortWindow = false;
                        sortWindowVisible = false;
                    }
                }

                if (filterWindowVisible)
                {
                    GUI.Window(FILTER_WINDOW_ID, filterWindowRect, filterWindowHandler, "Filter");

                    if (!mouseEnteredFilterWindow && filterWindowRect.Contains(mp))
                        mouseEnteredFilterWindow = true;
                    else if (mouseEnteredFilterWindow && 
                        (!filterWindowRect.Contains(mp) && !filterChildWindowVisible || 
                        !(filterWindowRect.Contains(mp) || filterChildWindowRect.Contains(mp))))
                        {
                            mouseEnteredFilterWindow = false;
                            filterWindowVisible = false;
                            filterChildWindowVisible = false;
                        }
                }

                if (filterChildWindowVisible)
                {
                    switch (filterChildType)
                    {
                        case "Mod":
                            modWindowScrollPosition = GUI.BeginScrollView(
                                modWindowRect, modWindowScrollPosition, modWindowViewRect);
                            filterChildWindowRect = modWindowRect;
                            GUIStyle labelStyle = new GUIStyle(HighLogic.Skin.label);
                            labelStyle.alignment = TextAnchor.MiddleCenter;
                            GUI.Label(new Rect(0, 0, 130, 30), "Mods to Hide", labelStyle);
                            filterChildWindowHandler(MOD_WINDOW_ID);
                            GUI.EndScrollView();
                            break;
                        case "Size":
                            filterChildWindowRect = sizeWindowRect;
                            GUI.Window(SIZE_WINDOW_ID, filterChildWindowRect, filterChildWindowHandler, "Sizes To Hide");
                            break;
                        case "Type":
                            filterChildWindowRect = typeWindowRect;
                            GUI.Window(TYPE_WINDOW_ID, filterChildWindowRect, filterChildWindowHandler, "Type To Show");
                            break;
                    }
                    if (!mouseEnteredFilterChildWindow && filterChildWindowRect.Contains(mp))
                        mouseEnteredFilterChildWindow = true;
                    else if (mouseEnteredFilterChildWindow && !filterChildWindowRect.Contains(mp))
                    {
                        mouseEnteredFilterChildWindow = false;
                        filterChildWindowVisible = false;
                    }
                }
                                             
            }
        }

        
        private void filterWindowHandler(int windowID)
        {
            if (GUI.Button(new Rect(10, 30, 130, 30), "Mod->"))
            {
                filterChildWindowVisible = true;
                filterChildType = "Mod";
                
            }

            if (GUI.Button(new Rect(10, 70, 130, 30), "Size->"))
            {
                filterChildWindowVisible = true;
                filterChildType = "Size";
            }

            if (GUI.Button(new Rect(10, 110, 130, 30), "Type->"))
            {
                filterChildWindowVisible = true;
                filterChildType = "Type";
            }
        }

        private void filterChildWindowHandler(int id)
        {
            Dictionary<string, ToggleState> states;
            
            switch (id)
            {
                case TYPE_WINDOW_ID:
                    states = types;
                    break;
                case SIZE_WINDOW_ID:
                    states = sizes;
                    break;
                case MOD_WINDOW_ID:
                    states = mods;
                    break;
                default:
                    return;
            }

            float newTop = 30f;

            if (id != TYPE_WINDOW_ID)
            { 
                if (GUI.Button(new Rect(10, 30, 130, 30), "Select All"))
                {
                    var names = new List<string>(states.Keys);
                    foreach (string name in names)
                    {
                        ToggleState state = states[name];
                        state.enabled = true;
                        states[name] = state;
                    }
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
                }

                newTop = 120f;
            }

            var keys = new List<string>(states.Keys);
            foreach (string name in keys)
            {
                ToggleState state = states[name];
                string truncatedName = name.Length > 15 ? name.Remove(14) : name;
                state.enabled = GUI.Toggle(new Rect(10, newTop, 130, 30), state.enabled, truncatedName, HighLogic.Skin.button);
                if (state.enabled && !state.latched)
                {
                    if (id == TYPE_WINDOW_ID)
                    {
                        foreach (string key in keys)
                        {
                            ToggleState s = states[key];
                            s.enabled = false;
                            states[key] = s;
                        }

                        typeSelected = name;
                    }

                    state.latched = true;
                    states[name] = state;                    
                    EditorPartList.Instance.Refresh();
                }
                else if (!state.enabled && state.latched)
                {
                    state.latched = false;
                    states[name] = state;

                    if (id == TYPE_WINDOW_ID)
                    {
                        typeSelected = "";
                    }

                    EditorPartList.Instance.Refresh();
                }
                newTop += 40;
            }
        }

        bool partInFilteredTypes(AvailablePart part)
        {
            if (String.IsNullOrEmpty(typeSelected))
                return true;

            switch (typeSelected)
            {
                case "Decoupler":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleDecouple"))
                        return true;
                    break;
                case "Engine":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleEngine"))
                        return true;
                    break;
                case "Docking":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleDockingNode"))
                        return true;
                    break;
                case "Fuel: Liquid":
                    if (part.partPrefab.Modules != null && part.partPrefab.Resources.Contains("LiquidFuel"))
                        return true;
                    break;
                case "Fuel: RCS":
                    if (part.partPrefab.Modules != null && part.partPrefab.Resources.Contains("MonoPropellant"))
                        return true;
                    break;
                case "RCS":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleRCS"))
                        return true;
                    break;
                case "Solar":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleDeployableSolarPanel"))
                        return true;
                    break;
                case "Landing Gear":
                    if (part.partPrefab.Modules != null && part.partPrefab.GetType().ToString() == "HLandingLeg")
                        return true;
                    break;
                case "Parachute":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleParachute"))
                        return true;
                    break;
                case "Wheel":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("ModuleWheel"))
                        return true;
                    break;
                case "Ladder":
                    if (part.partPrefab.Modules != null && part.partPrefab.Modules.Contains("RetractableLadder"))
                        return true;
                    break;
            }
            return false;
        }

        bool partInFilteredMods(AvailablePart part)
        {
            foreach (string mod in mods.Keys)
            {
                if (mods[mod].enabled && (findPartMod(part).ToUpper() == mod.ToUpper()))
                    return true;
            }
            return false;
        }

        private bool partInFilteredSizes(AvailablePart part)
        {
            foreach (string size in sizes.Keys)
            {
                if (sizes[size].enabled && (Math.Round(getPartSize(part), 2) == Math.Round(double.Parse(size), 2)))
                    return true;
            }
            return false;
        }

        string findPartMod(AvailablePart part)
        {
            string mod = "";
            UrlDir.UrlConfig config = Array.Find<UrlDir.UrlConfig>(configs, (c => part.name == c.name.Replace('_', '.')));
            if (config != null)
            {
                var id = new UrlDir.UrlIdentifier(config.url);
                mod = id[0];
            }
            
            return mod;
        }

        private void sortWindowHandler(int windowID)
        {
            // parts in the editor panels are drawn from the LoadedPartsList in order, so re-ordering the list
            // re-orders the parts in the panel
            List<AvailablePart> partList = PartLoader.LoadedPartsList;

            if (GUI.Button(new Rect(10, 30, 130, 30), "Default"))
            {
                partList.Sort((part1, part2) => (defaultPartSort.IndexOf(part1) - defaultPartSort.IndexOf(part2)));
                EditorPartList.Instance.Refresh();
            }

            if (GUI.Button(new Rect(10, 80, 130, 30), "Name"))
            {
                partList.Sort((part1, part2) => (String.Compare(part1.title, part2.title)));
                EditorPartList.Instance.Refresh();
            }

            if (GUI.Button(new Rect(10, 120, 130, 30), "Dry Mass"))
            {
                // Multiply by 1000000 so the float->int conversion doesn't lose precision
                // and add the index fudge factor to make the sort stable -- easier than writing my own merge sort
                // implementation. Likewise for most of these.
                partList.Sort((part1, part2) => (((int) (1000000 * (part1.partPrefab.mass - part2.partPrefab.mass)))
                    + (partList.IndexOf(part1) - partList.IndexOf(part2))));
                EditorPartList.Instance.Refresh();
            }

            if (GUI.Button(new Rect(10, 160, 130, 30), "Total Mass"))
            {
                // 1.0 fixing null reference exception in KSP 0.23
                AvailablePart kerbalEVA = null;
                AvailablePart flag = null;
                foreach (var part in partList)
                {
                    if (part.name == "kerbalEVA")
                        kerbalEVA = part;
                    else if (part.name == "flag")
                        flag = part;
                }
                if (kerbalEVA != null)
                    partList.Remove(kerbalEVA);
                if (flag != null)
                    partList.Remove(flag);
                
                partList.Sort((part1, part2) =>
                    (((int)(1000000 * ((part1.partPrefab.mass + part1.partPrefab.GetResourceMass()) 
                    - (part2.partPrefab.mass + part2.partPrefab.GetResourceMass()))))
                    + (partList.IndexOf(part1) - partList.IndexOf(part2))));
                EditorPartList.Instance.Refresh();
            }

            if (GUI.Button(new Rect(10, 200, 130, 30), "Manufacturer"))
            {
                partList.Sort((part1, part2) => (1000000 * (String.Compare(part1.manufacturer, part2.manufacturer))) 
                    + (partList.IndexOf(part1) - partList.IndexOf(part2)));
                EditorPartList.Instance.Refresh();
            }

            if (GUI.Button(new Rect(10, 240, 130, 30), "Size"))
            {
                partList.Sort((part1, part2) => (((int)(1000000 * (getPartSize(part1) - getPartSize(part2))))
                    + (partList.IndexOf(part1) - partList.IndexOf(part2))));
                EditorPartList.Instance.Refresh();
            }
        }

        // Calculates the average of the attach node sizes. This way, adaptors are bigger than
        // their small attachment node, but smaller than their large one.
        private float getPartSize(AvailablePart part)
        {
            float size = 0f;
            float count = 0f;

            if (part.partPrefab.attachNodes != null && part.partPrefab.attachNodes.Count > 0)
            {
                foreach (var attach in part.partPrefab.attachNodes)
                {
                    size += attach.size;
                    count++;
                }
                size = size / count;
            }

            return size;
        }
    }
}
