using System.Reflection;
using HarmonyLib;

namespace INeedWorkshopDeps;

public static class Preload {
	/// <summary>
	/// This method is called by the Doorstop loader before the game is loaded.
	/// </summary>
	// ReSharper disable once UnusedMember.Local
	private static void PreloadInit() {
		Logger.Log("Preload has started :P");
		Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
		Logger.Log("Harmony Patches have been applied");
	}
}