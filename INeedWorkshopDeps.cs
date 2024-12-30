using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace INeedWorkshopDeps;

[ContentWarningPlugin("stupidrepo.INeedWorkshopDeps", "0.1", true)]
public class INeedWorkshopDeps
{
    static INeedWorkshopDeps()
    {
    }
    
    public static void DisplayModalForMissingDeps()
    {
        if(Preload.erroredMods.Count == 0) return;
        
        var sb = new System.Text.StringBuilder();
        foreach (var erroredMod in Preload.erroredMods)
        {
            sb.AppendLine($"Mod with GUID \"{erroredMod.Item3}\" is missing the following dependencies:");
            foreach (var dep in erroredMod.Item2)
            {
                sb.AppendLine($"- {dep.Guid} (Workshop ID: {dep.WorkshopID})");
            }
            sb.AppendLine($"\"${erroredMod.Item3}\" has been disabled until these dependencies are installed and you have rebooted the game.");
        }
        
        ModalOption[] options = {
            new("Subscribe to all", DoSteamWorkshopDownload),
            new("Ignore")
        };
        Modal.Show("Missing dependencies", 
            "The following mods are missing dependencies. Please install them before continuing:\n" + sb.ToString(),
            options);
    }

    private static void DoSteamWorkshopDownload()
    {
        var missingDeps = (from erroredMod in Preload.erroredMods from dep in erroredMod.Item2 select dep.WorkshopID).ToList();
        foreach (var missingDep in missingDeps)
        {
            SteamUGC.SubscribeItem(new PublishedFileId_t(missingDep));
        }
    }
}