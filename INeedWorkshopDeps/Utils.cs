using INeedWorkshopDeps.Attributes;
using Steamworks;


namespace INeedWorkshopDeps;

public static class Utils
{
	public static void DoSteamWorkshopDownload()
	{
		var missingDeps = (from erroredMod in Preload.erroredMods from dep in erroredMod.Item2 select dep.WorkshopID)
			.ToList();

		foreach (var missingDep in missingDeps)
			SteamUGC.SubscribeItem(new PublishedFileId_t(missingDep));
	}

	public static async Task<List<SteamUGCDetails_t>> AskSteamToResolveDeps(List<ContentWarningDependency> neededDeps)
	{
		try
		{
			var resolvedDeps = new List<SteamUGCDetails_t>();
			var ugcDetailsRequest = SteamUGC.CreateQueryUGCDetailsRequest(neededDeps
					.Select(dep => new PublishedFileId_t(dep.WorkshopID)).ToList().ToArray(), // stupid Mono is conflicting with System.Linq
				(uint)neededDeps.Count
			);
			
			var result = await SteamUGC.SendQueryUGCRequest(ugcDetailsRequest).ToAsync<SteamUGCQueryCompleted_t>();
			if (result.m_eResult != EResult.k_EResultOK)
			{
				Modal.ShowError("Error", "An error occurred while trying to resolve dependencies: " + result.m_eResult);
				return new List<SteamUGCDetails_t>();
			}
			
			for (uint i = 0; i < result.m_unNumResultsReturned; i++)
				if (SteamUGC.GetQueryUGCResult(ugcDetailsRequest, i, out var details))
					resolvedDeps.Add(details);
			
			SteamUGC.ReleaseQueryUGCRequest(ugcDetailsRequest);
			return resolvedDeps;
		}
		catch (Exception e)
		{
			Modal.ShowError("Error", "An error occurred while trying to resolve dependencies: " + e.Message);
		}
		
		return new List<SteamUGCDetails_t>();
	}
}