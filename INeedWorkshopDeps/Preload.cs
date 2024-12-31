using System.Reflection;
using System.Text;
using HarmonyLib;
using INeedWorkshopDeps.Attributes;
using INeedWorkshopDeps.Errors;
using Mono.Cecil;
using Steamworks;
using UnityEngine;

namespace INeedWorkshopDeps;

public class Preload
{
	private static readonly List<Tuple<PublishedFileId_t, List<ContentWarningDependency>, string>>
		dependers = new(); // a list of: <The mod that depends on stuff, List<The things it depends on, but we don't know if loaded>, GUID of depender>

	public static List<Tuple<PublishedFileId_t, List<SteamUGCDetails_t>, string>>
		erroredMods =
			new(); // a list of: <The mod that depends on stuff, List<Missing dependencies>, GUID of depender>
	
	public static StringBuilder extraErrors = new();

	private static void PreloadInit()
	{
		// load Assembly-CSharp from game's data folder
		Debug.Log("Preload has started :P");

		var harmony = new Harmony("INeedWorkshopDeps.Harmony");
		harmony.PatchAll();
	}

	private static void DoNormalSteamLoad(List<Plugin> existingPlugins, PublishedFileId_t subscribedItem,
		List<PluginSubscribed> pluginSubscribedList)
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
			var perm = new GameObject("iNWD_PermObject");
		
			UnityEngine.Object.DontDestroyOnLoad(perm);
			perm.hideFlags = HideFlags.HideAndDontSave;
		
			perm.AddComponent<Components.ToastUtils>();
			
			__result = new List<PluginSubscribed>();
			foreach (var subscribedItem in PluginHandler.SubscribedItems)
			{
				var dependantModsKP = TryGetDeps(subscribedItem);
				if (dependantModsKP == null)
				{
					DoNormalSteamLoad(existingPlugins, subscribedItem,
						__result); // the normal steam load will add to __result
					continue;
				}

				var dependantMods = dependantModsKP.Value; // the key is the GUID of the mod
				if (dependantMods.Value == null) continue;

				Debug.LogWarning($"Found dependant mod: {subscribedItem}, it needs {dependantMods.Value.Count} mod" +
				                 $"{(dependantMods.Value.Count > 1 ? "s" : "")} to be loaded first");
				dependers.Add(new Tuple<PublishedFileId_t, List<ContentWarningDependency>, string>(subscribedItem,
					dependantMods.Value, dependantMods.Key));
			}
			
			return false;
		}

		[HarmonyPatch(nameof(PluginHandler.LoadSubscribedSteamworksPlugins))]
		[HarmonyPostfix]
		public static async void
			DoOurLoadPostfix(List<Plugin> existingPlugins,
				List<PluginSubscribed> __result)
		{
			var existingPluginsAndResultCombined = new List<Plugin>();
			existingPluginsAndResultCombined.AddRange(existingPlugins);
			existingPluginsAndResultCombined
				.AddRange(__result); // so we make our own thing and get existing plugins and the result combined

			// filter existingPluginsAndResultCombined to only have PluginSubscribed objects
			var existingPluginsAndResultCombinedFiltered = existingPluginsAndResultCombined
				.Where(plugin => plugin is PluginSubscribed).ToList();
			
			Debug.LogWarning("WE ARE NOW LOADING DEPENDENCY MODS");
			foreach (var depender in dependers)
			{
				try
				{
					await CheckDependerAndLoad(depender, existingPluginsAndResultCombinedFiltered, __result);
				} catch (Exception e)
				{
					Debug.LogException(e);
					extraErrors.AppendLine(e.Message);
					erroredMods.Add(
						new Tuple<PublishedFileId_t, List<SteamUGCDetails_t>, string>(depender.Item1,
							new List<SteamUGCDetails_t>(), depender.Item3));
				}
			}
			
			Debug.LogWarning("WE ARE DONE LOADING DEPENDENCY MODS");
			Utils.DisplayModalForMissingDeps();
		}

		private static async Task CheckDependerAndLoad(
			Tuple<PublishedFileId_t, List<ContentWarningDependency>, string> depender, List<Plugin> existingPlugins,
			List<PluginSubscribed> __result)
		{
			var resolvedDeps = await Utils.AskSteamToResolveDeps(depender
					.Item2, depender.Item3); // we check if the Workshop ID of the dependants are real
			// above function will throw if we couldn't resolve anything
			
			Debug.LogWarning($"All dependencies for {depender.Item3} have been resolved");
			Debug.LogWarning($"Now checking if dependencies are loaded");

			var missingDeps = resolvedDeps.Where(dep =>
				existingPlugins.All(plugin =>
					plugin.PublishedFileId!.Value != dep.m_nPublishedFileId));
			var missingDepsList = missingDeps.ToList();

			if (missingDepsList.Any())
			{
				Debug.LogWarning($"Not all dependencies for {depender.Item3} were found.");
				Debug.LogError(
					$"Missing dependencies: {string.Join(", ", missingDepsList.Select(dep => dep.m_rgchTitle))}");
				erroredMods.Add(
					new Tuple<PublishedFileId_t, List<SteamUGCDetails_t>, string>(depender.Item1,
						missingDepsList.ToList(), depender.Item3));
			}
			else
			{
				Debug.LogWarning("All dependencies for " + depender.Item3 + " have been loaded");
				DoNormalSteamLoad(existingPlugins, depender.Item1, __result);
			}
		}

		// check if mod is dependant by checking if it has ContentWarningDependency attribute
		public static KeyValuePair<string, List<ContentWarningDependency>?>? TryGetDeps(
			PublishedFileId_t publishedFileId)
		{
			if (!Plugin.LoadDirFromFileId(publishedFileId, out var directory) ||
			    !Plugin.FindDllInDirectory(directory, out var dllPath, out var ourGuid))
				return null;

			// load the assembly, check if it has attributes
			var assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath);

			List<ContentWarningDependency> dependencies = new();
			foreach (var type in assemblyDefinition.Modules.SelectMany(module => module.Types))
			{
				var customAttributes = type.CustomAttributes
					.Where(attr => attr.AttributeType.FullName == typeof(ContentWarningDependency).FullName).ToList();
				if (customAttributes.Count <= 0) continue; // no dependencies found, continue

				customAttributes.ForEach(attr =>
					dependencies.Add(new ContentWarningDependency((ulong)attr.ConstructorArguments.First().Value)));
			}

			return dependencies.Count > 0
				? new KeyValuePair<string, List<ContentWarningDependency>?>(ourGuid, dependencies)
				: null;
		}
	}

	[HarmonyPatch(typeof(Plugin))]
	public class PluginPatches
	{
		[HarmonyPatch(nameof(Plugin.LoadAssemblyFromFile))]
		[HarmonyPrefix]
		public static bool DontPatchHarmonyIfIAmMe(string dllPath, ref bool __result)
		{
			if (!dllPath.Contains("INeedWorkshopDeps"))
				return true;

			# if DEBUG
				Debug.LogWarning("Who do you think you're talking to right now?");
				Debug.LogWarning("Who is it you think you see?");
				Debug.LogWarning("Do you know how much I make a year?");
				Debug.LogWarning("I mean, even if I told you, you wouldn't believe it!");
				Debug.LogWarning("Do you know what would happen if I suddenly decided to stop going into work?");
				Debug.LogWarning("A business big enough that it could be listed on the NASDAQ goes belly up.");

				Debug.LogError("Disappears!");

				Debug.LogWarning("It ceases to exist without me.");
				Debug.LogWarning("No, you clearly don't know who you're talking to, so let me clue you in.");

				Debug.LogWarning("I am not in danger, Skyler.");
				Debug.LogError("I AM the danger.");

				Debug.LogWarning("A guy opens his door, and gets shot, and you think that of ME?");
				Debug.LogError("No, I am the one who knocks!");
			#endif

			Debug.LogError("I AM THE ONE WHO KNOCKS!");

			__result = false;
			return false;
		}
	}
}