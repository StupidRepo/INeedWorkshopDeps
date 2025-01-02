using System.Diagnostics;
using HarmonyLib;
using INeedWorkshopDeps.Attributes;
using Mono.Cecil;
using Sirenix.Utilities;
using Steamworks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace INeedWorkshopDeps.Preload;

[HarmonyPatch(typeof(PluginHandler))]
public class PluginHandlerPatches {
    private static bool HasShownError = false;
    /// <summary>
    /// Stop the game from hot loading mods, this would cause issues with the mod loading order. (and a lot of other things)
    /// </summary>
    /// <param name="param"> The item installed </param>
    /// <returns> False to stop the original method from running </returns>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(PluginHandler.OnItemInstalled))]
    public static bool OnItemInstalled(ItemInstalled_t param) {
        if (HasShownError) { return false; }
        Modal.ShowError("Hot Loading Disabled",
            "INeedWorkshopDeps disables the hot loading of mods.\n" +
            "Please restart the game to load the new mod.\n" +
            "Sorry!\n\n" +
            "This error will only show once per game session.");
        HasShownError = true;
        return false; 
    }
    
    /// <summary>
    /// Get all dependencies of a mod recursively using a depth-first search
    /// </summary>
    /// <param name="publishedFileId"> The mod to get dependencies of </param>
    /// <param name="dependencyTree"> The dependency tree </param>
    /// <param name="visited"> The visited nodes </param>
    /// <returns></returns>
    private static List<ulong> GetAllDependencies(ulong publishedFileId, Dictionary<ulong, List<ulong>> dependencyTree, List<ulong> visited) {
        List<ulong> dependencies = [];
        if (visited.Contains(publishedFileId)) {
            Debug.LogWarning($"Circular dependency detected, ignoring {publishedFileId}");
            return [];
        }
        
        if (!dependencyTree.TryGetValue(publishedFileId, out var contentWarningDependencies)) { return dependencies; }
        dependencies.AddRange(contentWarningDependencies);
        visited.Add(publishedFileId);

        foreach (var dependency in dependencyTree[publishedFileId]) {
            dependencies.AddRange(GetAllDependencies(dependency, dependencyTree, visited));
        }
        
        return dependencies;
    }
    
    /// <summary>
    /// Get the direct dependencies of a mod from the DLLs attributes
    /// </summary>
    /// <param name="publishedFileId"> The mod to get dependencies of </param>
    /// <returns></returns>
    private static List<ulong> GetDirectDependencies(PublishedFileId_t publishedFileId) {
        List<ulong> dependantMods = [];
        if (!Plugin.LoadDirFromFileId(publishedFileId, out var directory) || 
            !Plugin.FindDllInDirectory(directory, out var dllPath, out var _)) {
            return dependantMods;
        }

        // load the assembly, check if it has attributes
        var assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath);
        foreach (var modules in assemblyDefinition.Modules) {
            foreach (var type in modules.Types) {
                foreach (var attribute in type.CustomAttributes) {
                    if (attribute.AttributeType.Name == nameof(ContentWarningDependency)) {
                        dependantMods.Add((ulong)attribute.ConstructorArguments[0].Value);
                    }
                }
            }
        }
        
        return dependantMods;
    }

    /// <summary>
    /// All dependencies of all mods
    /// </summary>
    private static readonly List<ulong> AllDependencies = [];
    
    /// <summary>
    /// Mod to all dependencies
    /// </summary>
    private static readonly Dictionary<ulong, List<ulong>> ModToAllDependencies = new Dictionary<ulong, List<ulong>>();
    
    /// <summary>
    /// Load order of mods, lower number means higher priority, and the list is the mods to load.
    /// The list is sorted based on the workshop load order, while the priority is based on the number of dependencies.
    /// </summary>
    private static readonly Dictionary<int, List<ulong>> LoadOrder = new Dictionary<int, List<ulong>>();
    
    /// <summary>
    /// All subscribed items
    /// </summary>
    private static readonly PublishedFileId_t[] SubscribedItems = PluginHandler.SubscribedItems;

    /// <summary>
    /// Details of all subscribed items
    /// </summary>
    private static readonly Dictionary<PublishedFileId_t, SteamUGCDetails_t> SubscribedItemsDetails = new();
    
    /// <summary>
    /// Overwrite the loading of subscribed steamworks plugins
    /// </summary>
    /// <param name="existingPlugins"> The existing plugins, in this case the local plugins </param>
    /// <param name="__result"> What would be returned if this wasn't a harmony patch </param>
    /// <returns> False to stop the original method from running </returns>
    [HarmonyPatch(nameof(PluginHandler.LoadSubscribedSteamworksPlugins))]
    [HarmonyPrefix]
    public static bool LoadSubbedSWPluginsPatch(List<Plugin> existingPlugins, ref List<PluginSubscribed> __result) {
        __result = [];
        
        Logger.Log("Loading subscribed steamworks plugins");
        
        // Generate dependency tree
        Logger.Log("Generating dependency tree");
        
        var dependencyTree = new Dictionary<ulong, List<ulong>>();
        foreach (var subscribedItem in SubscribedItems) {
            var list = GetDirectDependencies(subscribedItem);
            dependencyTree[(ulong)subscribedItem] = list;
        }
        Logger.Log("Dependency tree generated");
        
        // Resolve dependencies
        Logger.Log("Resolving dependencies");
        foreach (var (modId, value) in dependencyTree) {
            var dependencies = GetAllDependencies(modId, dependencyTree, []);
            var count = dependencies.Count;
            
            if (!LoadOrder.ContainsKey(count)) { LoadOrder[count] = []; }
            LoadOrder[count].Add(modId);

            foreach (var dependency in dependencies.Where(dependency => !AllDependencies.Contains(dependency)))
                AllDependencies.Add(dependency);
            
            ModToAllDependencies[modId] = dependencies;
        }
        Logger.Log("Dependencies resolved");
        
        return false;
    }
    
    /// <summary>
    /// Postfix for loading subscribed steamworks plugins, handles the async parts of the loading.
    /// </summary>
    /// <param name="existingPlugins"> The existing plugins, in this case the local plugins </param>
    /// <param name="__result"> What would be returned if this wasn't a harmony patch </param>
    [HarmonyPatch(nameof(PluginHandler.LoadSubscribedSteamworksPlugins))]
    [HarmonyPostfix]
    public static async void OurLoadLogic(List<Plugin> existingPlugins, List<PluginSubscribed> __result) {
        try {
            // Get the details of all subscribed items
            Logger.Log("Getting details of all subscribed items");
            var ugcReq = SteamUGC.CreateQueryUGCDetailsRequest(SubscribedItems, (uint)SubscribedItems.Length);
            var ugcResult = await SteamUGC.SendQueryUGCRequest(ugcReq).ToAsync<SteamUGCQueryCompleted_t>();
            for (uint i = 0; i < ugcResult.m_unNumResultsReturned; i++) {
                if (SteamUGC.GetQueryUGCResult(ugcReq, i, out var details)) {
                    SubscribedItemsDetails[details.m_nPublishedFileId] = details;
                }
            }
            SteamUGC.ReleaseQueryUGCRequest(ugcReq);
            Logger.Log("Details of all subscribed items gotten");
            
            // Get steam to resolve workshop mods
            Logger.Log("Asking steam to resolve workshop mods");
            var resolvedSteamWorkshopDeps = await AskSteamToResolveWorkshopMods(AllDependencies);
            var allMissingWorkshopDeps = resolvedSteamWorkshopDeps.Where(dep => SubscribedItems.All(sub => sub != dep.m_nPublishedFileId)).ToList();
            
            Logger.Log("Steam resolved workshop mods");
            Logger.LogWarning(allMissingWorkshopDeps.Count > 0
                ? $"Missing dependencies: {string.Join(", ", allMissingWorkshopDeps.Select(dep => dep.m_rgchTitle))}"
                : "No missing dependencies");            

            // Check if the mods are subscribed to
            Logger.Log("Checking each subscribed mod to see if it requires deps");
            var missingModsToWorkshopDeps = new Dictionary<ulong, List<SteamUGCDetails_t>>();
            SubscribedItems.ForEach(mod =>
            {
                Logger.Log($"Checking mod {mod}");
                // for each depender requiring dependencies, check if the dependencies are missing
                // if there are, add it to missingModsToWorkshopDeps where Dictionary<ulong, List<ulong>> is the depender -> the missing deps of the depender
                var missingDeps = ModToAllDependencies[mod.m_PublishedFileId]
                    .Where(dep => allMissingWorkshopDeps.Any(missingDep => (ulong)missingDep.m_nPublishedFileId == dep)).ToList();
                var missingDepsAsSteamUGCDetails = resolvedSteamWorkshopDeps
                    .Where(dep => missingDeps.Contains((ulong)dep.m_nPublishedFileId))
                    .ToList();
                if (missingDeps.Count <= 0) return;
                
                Logger.LogWarning($"Mod {SubscribedItemsDetails[mod]} is missing dependencies {string.Join(", ", missingDeps)}");
                missingModsToWorkshopDeps[(ulong)mod] = missingDepsAsSteamUGCDetails;
            });
            Logger.Log("Mods checked");
            
            Logger.Log("Showing modals if needed");
            var didPressSubToAll = false;
            missingModsToWorkshopDeps.ForEach(kv =>
            {
                var depender = kv.Key;
                var missingDeps = kv.Value;
                ToastUtilities.EnqueueToast(
                    SubscribedItemsDetails[new PublishedFileId_t(depender)].m_rgchTitle,
                    $"{SubscribedItemsDetails[new PublishedFileId_t(depender)].m_rgchTitle} is missing dependencies:\n" + string.Join("\n", missingDeps.Select(dep => $"{dep.m_rgchTitle} (ID: {dep.m_nPublishedFileId})"))
                    + "\n\nYou can either:\n- Ignore this and the mod will not be loaded\n- Subscribe to the missing mods",
                    [
                        new ModalOption("Ignore"),
                        new ModalOption("Subscribe to all", () =>
                        {
                            didPressSubToAll = true;
                            missingDeps.ForEach(dep => SteamUGC.SubscribeItem(dep.m_nPublishedFileId));
                        }),
                    ]
                );
            });
            if (didPressSubToAll)
            {
                ToastUtilities.EnqueueToast(
                    "Restart Required",
                    "Please restart the game to load mods that had missing dependencies",
                    [new ModalOption("Okay", RestartGame)]
                );
            }

            Logger.Log("Loading mods");
            var priorityToMods = LoadOrder.OrderBy(kv => kv.Key).ToArray();
            
            foreach (var (priority, mods) in priorityToMods) {
                Logger.Log($"Loading all mods with priority {priority}");
                
                foreach (var modId in mods) {
                    Logger.Log($"Loading mod {modId}");
                    
                    if (!PluginSubscribed.Load((PublishedFileId_t)(modId), existingPlugins, out var result)) { continue; }
                    
                    __result.Add(result);
                    PluginHandler.needsHashRefresh = true;
                }
            }
            Logger.Log("Mods loaded");
        }
        catch (Exception) { /* ignored */ } // we don't want to crash the game and unhandled exceptions in async methods will crash the game
    }

    /// <summary>
    /// Ask steam to resolve workshop mods, this is an async method and should be awaited.
    /// What resolving means is that we ask steam to get the details of the mods we need.
    /// </summary>
    /// <param name="neededMods"> The mods we need to resolve </param>
    /// <returns> The resolved mods </returns>
    private static async Task<List<SteamUGCDetails_t>> AskSteamToResolveWorkshopMods(List<ulong> neededMods) {
        List<SteamUGCDetails_t> resolvedMods = [];
        var ugcDetailsRequest = SteamUGC.CreateQueryUGCDetailsRequest(
            neededMods.Select(dep => new PublishedFileId_t(dep)).ToArray(), 
            (uint)neededMods.Count
        );
        var result = await SteamUGC.SendQueryUGCRequest(ugcDetailsRequest).ToAsync<SteamUGCQueryCompleted_t>();        
        for (uint i = 0; i < result.m_unNumResultsReturned; i++) {
            if (SteamUGC.GetQueryUGCResult(ugcDetailsRequest, i, out var details)) {
                resolvedMods.Add(details);
            }
        }
        SteamUGC.ReleaseQueryUGCRequest(ugcDetailsRequest);
        return resolvedMods;
    }

    private static void RestartGame()
    {
        SteamAPI.Shutdown();
        Application.Quit(221); // 221 = random exit code lmao.
        Process.Start(new ProcessStartInfo
        {
            FileName = "steam://rungameid/2881650",
            UseShellExecute = true
        });
    }
}

[HarmonyPatch(typeof(Plugin))]
public class PluginPatches {
    /// <summary>
    /// Avoid repatching our own DLL
    /// </summary>
    /// <param name="dllPath"> The path to the DLL </param>
    /// <param name="__result"> What would be returned if this wasn't a harmony patch </param>
    /// <returns> False to stop the original method from running, true to continue </returns>
    [HarmonyPatch(nameof(Plugin.LoadAssemblyFromFile))]
    [HarmonyPrefix]
    public static bool AvoidRepatch(string dllPath, ref bool __result) {
        if (!dllPath.EndsWith("INeedWorkshopDeps.preload.dll")) { return true; }
        
        Logger.Log("Found our own DLL, not loading it");
        __result = false;
        return false;
    }
}