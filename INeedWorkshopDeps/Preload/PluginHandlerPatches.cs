using HarmonyLib;
using INeedWorkshopDeps.Attributes;
using Mono.Cecil;
using Steamworks;
using UnityEngine;

namespace INeedWorkshopDeps;

[HarmonyPatch(typeof(PluginHandler))]
public class PluginHandlerPatches {
    private static bool HasShownError = false;
    /// <summary>
    /// Stop the game from hot loading mods, this would cause issues with the mod loading order. (and a lot of other things)
    /// </summary>
    /// <param name="param"> The item installed </param>
    /// <returns> False to stop the original method from running </returns>
    [HarmonyPrefix]
    [HarmonyPatch("OnItemInstalled")]
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
        if (!dependencyTree.TryGetValue(publishedFileId, out List<ulong>? contentWarningDependencies)) { return dependencies; }
        dependencies.AddRange(contentWarningDependencies);
        visited.Add(publishedFileId);

        foreach (ulong dependency in dependencyTree[publishedFileId]) {
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
        if (!Plugin.LoadDirFromFileId(publishedFileId, out string? directory) || 
            !Plugin.FindDllInDirectory(directory, out string? dllPath, out string? _)) {
            return dependantMods;
        }

        // load the assembly, check if it has attributes
        AssemblyDefinition? assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath);
        foreach (ModuleDefinition? modules in assemblyDefinition.Modules) {
            foreach (TypeDefinition? type in modules.Types) {
                foreach (CustomAttribute? attribute in type.CustomAttributes) {
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
    /// Overwrite the loading of subscribed steamworks plugins
    /// </summary>
    /// <param name="existingPlugins"> The existing plugins, in this case the local plugins </param>
    /// <param name="__result"> What would be returned if this wasn't a harmony patch </param>
    /// <returns> False to stop the original method from running </returns>
    [HarmonyPatch("LoadSubscribedSteamworksPlugins")]
    [HarmonyPrefix]
    public static bool LoadSubscribedSteamworksPluginsOverwrite(List<Plugin> existingPlugins, ref List<PluginSubscribed> __result) {
        __result = [];
        
        Logger.Log("Loading subscribed steamworks plugins");
        
        // Generate dependency tree
        Logger.Log("Generating dependency tree");
        Dictionary<ulong, List<ulong>> dependencyTree = new Dictionary<ulong, List<ulong>>();
        foreach (PublishedFileId_t subscribedItem in SubscribedItems) {
            List<ulong> list = GetDirectDependencies(subscribedItem);
            dependencyTree[(ulong)subscribedItem] = list;
        }
        Logger.Log("Dependency tree generated");
        
        // Resolve dependencies
        Logger.Log("Resolving dependencies");
        foreach ((ulong modId, List<ulong> value) in dependencyTree) {
            List<ulong> dependencies = GetAllDependencies(modId, dependencyTree, []);
            int count = dependencies.Count;
            
            if (!LoadOrder.ContainsKey(count)) { LoadOrder[count] = []; }
            LoadOrder[count].Add(modId);

            foreach (ulong dependency in dependencies) {
                if (AllDependencies.Contains(dependency)) { continue; }
                AllDependencies.Add(dependency);
            }
            
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
    [HarmonyPatch("LoadSubscribedSteamworksPlugins")]
    [HarmonyPostfix]
    public static async void DoOurLoadPostfix(List<Plugin> existingPlugins, List<PluginSubscribed> __result) {
        try {
            // Get steam to resolve workshop mods
            Logger.Log("Asking steam to resolve workshop mods");
            List<SteamUGCDetails_t> steamWorkshopMods = await AskSteamToResolveWorkshopMods(AllDependencies);
            Logger.Log("Steam resolved workshop mods");

            // Check if the mods are subscribed to
            Logger.Log("Checking if mods are subscribed to");
            foreach (SteamUGCDetails_t mod in steamWorkshopMods) {
                if (mod.m_eResult == EResult.k_EResultOK) {
                    // Check if the mod is subscribed to
                    if (SubscribedItems.Any(subscribedItem => subscribedItem == mod.m_nPublishedFileId)) {
                        continue; // mod is subscribed to so we can continue
                    }
                    
                    // mod is not subscribed to so we need to warn the user
                    Logger.LogWarning($"Mod {mod.m_rgchTitle} ({mod.m_nPublishedFileId}) is not subscribed to");
                    ToastUtilities.EnqueueToast(
                        "Unsubscribed mod",
                        $"Mod {mod.m_rgchTitle} ({mod.m_nPublishedFileId}) is not subscribed to.\n" +
                        $"If you subscribed to this mod, please restart the game.",
                        [
                            new ModalOption("Ignore"),
                            new ModalOption("Subscribe", () => { SteamUGC.SubscribeItem(mod.m_nPublishedFileId); }),
                        ]);
                }
                else {
                    // failed to resolve mod
                    Logger.LogWarning($"Failed to resolve mod {mod.m_nPublishedFileId}");
                    foreach ((ulong modId, List<ulong> dependencies) in ModToAllDependencies) {
                        ulong missingModId = (ulong)mod.m_nPublishedFileId;
                        if (!dependencies.Contains(missingModId)) {
                            continue;
                        }

                        Logger.LogWarning(
                            $"Mod with ID \"{modId}\" has a dependency that doesn't exist on the workshop: {missingModId})");
                        ToastUtilities.EnqueueToast(
                            "Missing dependency",
                            $"Mod with ID \"{modId}\" has a dependency that doesn't exist on the workshop: {missingModId}",
                            [new ModalOption("OK")]
                        );

                        dependencies.Remove(missingModId);
                    }
                }
            }
            Logger.Log("Mods checked");
            
            Logger.Log("Loading mods");
            KeyValuePair<int, List<ulong>>[] priorityToMods = LoadOrder.OrderBy(kv => kv.Key).ToArray();
            foreach ((int priority, List<ulong>? mods) in priorityToMods) {
                Logger.Log($"Loading all mods with priority {priority}");
                foreach (ulong modId in mods) {
                    Logger.Log($"Loading mod {modId}");
                    if (!PluginSubscribed.Load((PublishedFileId_t)(modId), existingPlugins, out PluginSubscribed? result)) { return; }
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
        UGCQueryHandle_t ugcDetailsRequest = SteamUGC.CreateQueryUGCDetailsRequest(
            neededMods.Select(dep => new PublishedFileId_t(dep)).ToArray(), 
            (uint)neededMods.Count
        );
        SteamUGCQueryCompleted_t result = await SteamUGC.SendQueryUGCRequest(ugcDetailsRequest).ToAsync<SteamUGCQueryCompleted_t>();        
        for (uint i = 0; i < result.m_unNumResultsReturned; i++) {
            if (SteamUGC.GetQueryUGCResult(ugcDetailsRequest, i, out SteamUGCDetails_t details)) {
                resolvedMods.Add(details);
            }
        }
        SteamUGC.ReleaseQueryUGCRequest(ugcDetailsRequest);
        return resolvedMods;
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