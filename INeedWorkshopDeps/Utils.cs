using System.Text;
using INeedWorkshopDeps.Attributes;
using INeedWorkshopDeps.Components;
using INeedWorkshopDeps.Errors;
using Steamworks;
using UnityEngine;


namespace INeedWorkshopDeps;

public static class Utils
{
	public static void DoSteamWorkshopDownload()
	{
		foreach (var erroredMod in Preload.erroredMods)
			SteamUGC.SubscribeItem(erroredMod.Item1);
	}

	public static async Task<List<SteamUGCDetails_t>> AskSteamToResolveDeps(List<ContentWarningDependency> neededDeps,
		string dependerGuid)
	{
		var resolvedDeps = new List<SteamUGCDetails_t>();
		var ugcDetailsRequest = SteamUGC.CreateQueryUGCDetailsRequest(neededDeps
				.Select(dep => new PublishedFileId_t(dep.WorkshopID)).ToList()
				.ToArray(), // stupid Mono is conflicting with System.Linq
			(uint)neededDeps.Count
		);

		var result = await SteamUGC.SendQueryUGCRequest(ugcDetailsRequest).ToAsync<SteamUGCQueryCompleted_t>();
		if (result.m_eResult != EResult.k_EResultOK || result.m_unNumResultsReturned != neededDeps.Count)
			throw new DependenciesNotResolvedException(neededDeps.ToArray(), dependerGuid);

		for (uint i = 0; i < result.m_unNumResultsReturned; i++)
			if (SteamUGC.GetQueryUGCResult(ugcDetailsRequest, i, out var details))
				resolvedDeps.Add(details);

		SteamUGC.ReleaseQueryUGCRequest(ugcDetailsRequest);
		return resolvedDeps;
	}

	public static void DisplayModalForMissingDeps()
	{
		if (Preload.erroredMods.Count == 0)
		{
			Debug.LogWarning("No mods with missing dependencies found.");
			return;
		}

		var sb = new StringBuilder();
		foreach (var erroredMod in Preload.erroredMods)
		{
			sb.AppendLine($"Mod with GUID \"{erroredMod.Item3}\" is missing the following dependencies:");
			foreach (var dep in erroredMod.Item2)
			{
				sb.AppendLine($"- {dep.m_rgchTitle} (Workshop ID: {dep.m_nPublishedFileId})");
			}
			sb.AppendLine($"\"{erroredMod.Item3}\" has been disabled until these dependencies are installed and you have rebooted the game.");
		}

		ModalOption[] options =
		{
			new("Subscribe to missing", () =>
			{
				DoSteamWorkshopDownload();
				DisplayModalForExtraErrors();
			}),
			new("Ignore", () =>
			{
				DisplayModalForExtraErrors();
			})
		};
		ToastUtils.Instance.EnqueueToast("Missing dependencies",
			"The following mods are missing dependencies. Please install them before continuing:\n" + sb,
			options);
	}

	public static void DisplayModalForExtraErrors()
	{
		if (Preload.extraErrors.Length == 0)
		{
			Debug.LogWarning("No extra errors found.");
			return;
		}
		
		var sb = new StringBuilder();
		
		sb.AppendLine("The following errors occurred while loading mods:");
		sb.AppendLine(Preload.extraErrors.ToString());
		
		sb.AppendLine("Please report these errors to the mod authors of the mods listed above.");
		
		ToastUtils.Instance.EnqueueToast("Extra errors occurred", sb.ToString(), new ModalOption[] { new("OK") });
	}
}