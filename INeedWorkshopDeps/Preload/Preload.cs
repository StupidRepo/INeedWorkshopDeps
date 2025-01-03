using System.Reflection;
using HarmonyLib;

namespace INeedWorkshopDeps.Preload;

public static class Preload {
	/// <summary>
	/// This method is called by the game's preloader before the game is loaded.
	/// </summary>
	// ReSharper disable once UnusedMember.Local
	private static void PreloadInit() {
		Logger.Logger.Log("Preload has started :P");
		Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
		
		Logger.Logger.Log("Harmony patches have been applied");
	}
}