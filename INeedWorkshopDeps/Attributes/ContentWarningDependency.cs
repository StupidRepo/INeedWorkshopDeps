namespace INeedWorkshopDeps.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ContentWarningDependency : Attribute
{
	public ContentWarningDependency(ulong workshopID)
	{
		WorkshopID = workshopID;
	}

	public ulong WorkshopID { get; set; }
}