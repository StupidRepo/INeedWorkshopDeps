namespace INeedWorkshopDeps.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ContentWarningDependency(ulong workshopID) : Attribute {
	public ulong WorkshopID { get; } = workshopID;
}