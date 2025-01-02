using INeedWorkshopDeps.Attributes;

namespace INeedWorkshopDeps.Errors;

public class DependenciesNotResolvedException : Exception
{
	public DependenciesNotResolvedException(ContentWarningDependency[] nonResolvedDeps, string dependerGuid) : 
		base($"Could not resolve dependencies for mod with GUID of {dependerGuid}: {string.Join(", ", nonResolvedDeps.Select(dep => dep.WorkshopID))}" +
		$"\nMaybe the dev input the ID wrong?")

	{
	}
}