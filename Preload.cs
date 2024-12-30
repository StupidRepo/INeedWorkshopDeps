using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace INeedWorkshopDeps;

public class Preload
{
	static List<Tuple<PublishedFileId_t, List<ContentWarningDependency>, string>> dependers = new(); // a list of: <The mod that depends on stuff, List<The things it depends on, but we don't know if loaded>, GUID of depender>
	public static List<Tuple<PublishedFileId_t, List<ContentWarningDependency>, string>> erroredMods = new(); // a list of: <The mod that depends on stuff, List<The things it depends on, but are not loaded>, GUID of depender>

    private static void PreloadInit()
    {
        // Harmony.CreateAndPatchAll(typeof(Preload));
        Debug.Log("Hello from INeedWorkshopDeps! This is called on plugin load");
    }
    
    private static void DoNormalSteamLoad(List<Plugin> existingPlugins, PublishedFileId_t subscribedItem, List<PluginSubscribed> pluginSubscribedList)
    {
	    if (!PluginSubscribed.Load(subscribedItem, existingPlugins, out var result)) return;
    
	    pluginSubscribedList.Add(result);
	    PluginHandler.needsHashRefresh = true;
    }
    
    [HarmonyPatch(typeof(PluginHandler))]
    public class PluginLoadingBlaBlaPatches
    {
    	[HarmonyPatch(nameof(PluginHandler.LoadSubscribedSteamworksPlugins))]
    	[HarmonyPrefix]
    	public static bool DoOurLoad(List<Plugin> existingPlugins, ref List<PluginSubscribed> __result)
    	{
			__result = new List<PluginSubscribed>();
    		foreach (var subscribedItem in PluginHandler.SubscribedItems)
    		{
    			var dependantModsKP = TryGetDeps(subscribedItem);
    			if (dependantModsKP == null)
			    {
				    DoNormalSteamLoad(existingPlugins, subscribedItem, __result); // the normal steam load will add to __result
				    continue;
			    }
			    
			    var dependantMods = dependantModsKP.Value; // the key is the GUID of the mod
			    if(dependantMods.Value == null) continue;

			    Debug.LogWarning($"Found dependant mod: {subscribedItem}, it needs {dependantMods.Value.Count} mod" +
			                     $"{(dependantMods.Value.Count > 1 ? "s" : "")} to be loaded first");
			    dependers.Add(new Tuple<PublishedFileId_t, List<ContentWarningDependency>, string>(subscribedItem, dependantMods.Value, dependantMods.Key));
    		}
    		return false;
    	}

	    [HarmonyPatch(nameof(PluginHandler.LoadSubscribedSteamworksPlugins))]
	    [HarmonyPostfix]
	    public static void DoOurLoadPostfix(List<Plugin> existingPlugins, ref List<PluginSubscribed> __result) // here, result should be previously loaded STEAM mods
	    {
		    var existingPluginsAndResultCombined = new List<Plugin>();
		    existingPluginsAndResultCombined.AddRange(existingPlugins);
		    existingPluginsAndResultCombined.AddRange(__result); // so we make our own thing and get existing plugins and the result combined
		    
		    Debug.LogWarning("WE ARE NOW LOADING DEPENDENCY MODS");
		    foreach (var depender in dependers)
		    {
			    var missingDeps = new List<ContentWarningDependency>();
			    foreach (var potentialMissingDependency in depender.Item2)
			    {
				    if (existingPluginsAndResultCombined.Any(m => m.InfoFromAssembly?.guid == potentialMissingDependency.Guid)) continue; // we found the dependency in the existing plugins, continue
				    missingDeps.Add(potentialMissingDependency); // we didn't find the dependency in the existing plugins, add to missingDeps
			    }

			    if (missingDeps.Count <= 0)
			    {
				    Debug.LogWarning($"ALL DEPENDENCIES FOUND FOR MOD WITH GUID: {depender.Item3}");
				    DoNormalSteamLoad(existingPlugins, depender.Item1, __result);
				    
				    continue;
			    };
			    
			    erroredMods.Add(new Tuple<PublishedFileId_t, List<ContentWarningDependency>, string>(depender.Item1, missingDeps, depender.Item3));
			    Debug.LogWarning($"MISSING DEPENDENCIES FOR MOD WITH GUID: {depender.Item3}:");
			    Debug.LogError(string.Join(", ", missingDeps.Select(m => m.Guid)));
		    }
		    
		    Debug.LogWarning("WE ARE DONE LOADING DEPENDENCY MODS");
		    INeedWorkshopDeps.DisplayModalForMissingDeps();
	    }
    
    	// check if mod is dependant by checking if it has ContentWarningDependency attribute
    	public static KeyValuePair<string, List<ContentWarningDependency>?>? TryGetDeps(
    		PublishedFileId_t publishedFileId)
    	{ 
		    if (!Plugin.LoadDirFromFileId(publishedFileId, out var directory) || !Plugin.FindDllInDirectory(directory, out var dllPath, out var ourGuid))
    			return null;
    
		    // load the assembly, check if it has attributes
		    var assembly = Assembly.LoadFile(dllPath);
		    
		    List<ContentWarningDependency> dependencies = new();
		    foreach (var type in assembly.GetTypes())
		    {
			    var customAttributes = type.GetCustomAttributes<ContentWarningDependency>().ToList();
			    if (customAttributes.Count <= 0) continue; // no dependencies found, continue
			    
			    dependencies.AddRange(customAttributes);
		    }
		    
		    return dependencies.Count > 0 ? new KeyValuePair<string, List<ContentWarningDependency>?>(ourGuid, dependencies) : null;
	    }
    }
}