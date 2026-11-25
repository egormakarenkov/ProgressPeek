using System;using System.IO;using System.Web.Script.Serialization;
namespace ProgressPeek {
 public static class StateMigration {
  static void SafePath(string path){string full=Path.GetFullPath(path);for(string p=full;!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked migration path");}
  public static string CopyOnce(string legacy,string destination){
   try{if(!Directory.Exists(legacy))return "";SafePath(legacy);SafePath(destination);Directory.CreateDirectory(destination);string marker=Path.Combine(destination,"migration.complete");if(File.Exists(marker))return "";
    // Settings and usage only: never move credentials, transcripts, reports or executable stages.
    foreach(string name in new[]{"settings.json","checks.json"}){string source=Path.Combine(legacy,name),target=Path.Combine(destination,name);if(!File.Exists(source)||File.Exists(target))continue;SafePath(source);if(new FileInfo(source).Length>1048576)throw new IOException("Legacy preferences exceed safe migration limit");string text=File.ReadAllText(source);var json=new JavaScriptSerializer();if(name=="settings.json"){var settings=json.Deserialize<Settings>(text);if(settings==null)throw new IOException("Invalid legacy preferences");settings.SetupCompleted=true;text=json.Serialize(settings);}else if(json.Deserialize<Ledger>(text)==null)throw new IOException("Invalid legacy usage ledger");string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text);File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}}
    File.WriteAllText(marker,"Preferences copied; original retained.");return "";
   }catch{return "Legacy preferences could not all be copied. Originals are retained; close both versions and retry or recover settings.json/checks.json manually.";}
  }
 }
 public static class SetupConsent {
  public static bool AllowsAi(Settings settings){return settings.SetupCompleted&&settings.Enabled;}
  public static void Finish(Storage storage,bool enable){bool oldDone=storage.Settings.SetupCompleted,oldEnabled=storage.Settings.Enabled;storage.Settings.SetupCompleted=true;storage.Settings.Enabled=enable;try{storage.SaveSettings();}catch{storage.Settings.SetupCompleted=oldDone;storage.Settings.Enabled=oldEnabled;throw;}}
 }
}
