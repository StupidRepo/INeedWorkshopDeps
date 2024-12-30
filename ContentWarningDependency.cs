namespace INeedWorkshopDeps;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ContentWarningDependency : Attribute
{
	public string Guid { get; set; }

	public ulong WorkshopID { get; set; }

	public ContentWarningDependency(string guid, ulong workshopID)
	{
		this.Guid = guid;
		this.WorkshopID = workshopID;
	}
}
