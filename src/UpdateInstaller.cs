using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
namespace ProgressPeek {
 public class UpdateState {public DateTime LastAttemptUtc;public string PendingId="";public bool Automatic;public string Status="";}
 class UpdatePending {public string Target,OriginalHash,CurrentVersion;public bool IncludeBeta;}
 public sealed class UpdateStaging {
  readonly string root;public UpdateState State {get;private set;}
  public UpdateStaging(string folder){root=Path.Combine(Path.GetFullPath(folder),"updates");UpdateSecurity.SafePath(root);Directory.CreateDirectory(root);State=Read<UpdateState>(Path.Combine(root,"state.json"))??new UpdateState();}
  internal static T Read<T>(string path)where T:class{try{UpdateSecurity.SafePath(path);if(new FileInfo(path).Length>16384)return null;return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path));}catch{return null;}}
  internal static void Atomic(string path,object value){UpdateSecurity.SafePath(path);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public void Save(){Atomic(Path.Combine(root,"state.json"),State);}
  public void Disarm(){State.Automatic=false;Save();}
  public bool RevalidateChannel(bool beta){
   if(String.IsNullOrEmpty(State.PendingId))return false;
   try{string directory=PendingDirectory();UpdateSecurity.VerifyManifest(UpdateInstaller.ReadBounded(Path.Combine(directory,"manifest.json"),UpdateSecurity.ManifestLimit),UpdateInstaller.ReadBounded(Path.Combine(directory,"manifest.sig"),UpdateSecurity.SignatureLimit),DistributionConfig.Repository,DistributionConfig.UpdatePublicKeyXml,BetaDiagnostics.Version,beta);return true;}
   catch(InvalidDataException){State.PendingId="";State.Automatic=false;State.Status="Staged update is no longer compatible with your release channel. Check again for a compatible update.";Save();return false;}
  }
  internal string PendingDirectory(){Guid id;if(!Guid.TryParseExact(State.PendingId,"N",out id))throw new InvalidDataException("Invalid staged update.");string path=Path.Combine(root,id.ToString("N"));UpdateSecurity.SafePath(path);return path;}
  public void Stage(UpdateCandidate candidate,byte[] bytes,string target,bool beta,bool automatic){
   if(!UpdateSecurity.Configured)throw new InvalidDataException("Updates are not configured.");var verified=UpdateSecurity.VerifyManifest(candidate.ManifestBytes,candidate.Signature,DistributionConfig.Repository,DistributionConfig.UpdatePublicKeyXml,BetaDiagnostics.Version,beta);if(bytes.LongLength!=verified.Size||UpdateSecurity.Hash(bytes)!=verified.Sha256)throw new InvalidDataException("Downloaded executable failed verification.");target=Path.GetFullPath(target);UpdateSecurity.SafePath(target);if(!String.Equals(target,Process.GetCurrentProcess().MainModule.FileName,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(target)!=UpdateSecurity.ExecutableName)throw new InvalidDataException("This location requires a manual download.");
   // Probe the actual target directory without requesting elevation or touching the installed executable.
   if(!String.IsNullOrEmpty(State.PendingId))try{string existing=PendingDirectory();var pending=Read<UpdatePending>(Path.Combine(existing,"pending.json"));if(pending!=null&&pending.Target==target&&pending.CurrentVersion==BetaDiagnostics.Version&&pending.OriginalHash==UpdateSecurity.HashFile(target)&&UpdateSecurity.HashFile(Path.Combine(existing,"helper.exe"))==pending.OriginalHash&&UpdateSecurity.Hash(UpdateInstaller.ReadBounded(Path.Combine(existing,"manifest.json"),UpdateSecurity.ManifestLimit))==UpdateSecurity.Hash(candidate.ManifestBytes)&&UpdateSecurity.Hash(UpdateInstaller.ReadBounded(Path.Combine(existing,"manifest.sig"),UpdateSecurity.SignatureLimit))==UpdateSecurity.Hash(candidate.Signature)){UpdateSecurity.VerifyFile(Path.Combine(existing,"candidate.exe"),verified);State.Automatic=automatic;Save();return;}}catch(InvalidDataException){}catch(IOException){}
   string probe=Path.Combine(Path.GetDirectoryName(target),".ProgressPeek-update-"+Guid.NewGuid().ToString("N")+".tmp");try{using(var f=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}File.Delete(probe);}catch{throw new IOException("This installation folder is not writable. Use a manual download.");}
   string id=WritePackage(root,candidate,bytes,target,verified,BetaDiagnostics.Version,beta);State.PendingId=id;State.Automatic=automatic;State.Status="Verified update ready. Restart now or apply on a later launch.";Save();
  }
  internal static string WritePackage(string root,UpdateCandidate candidate,byte[] bytes,string target,VerifiedUpdate verified,string current,bool beta){
   if(bytes.LongLength!=verified.Size||UpdateSecurity.Hash(bytes)!=verified.Sha256)throw new InvalidDataException("Downloaded executable failed verification.");UpdateSecurity.SafePath(root);UpdateSecurity.SafePath(target);string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(root,id);Directory.CreateDirectory(directory);UpdateSecurity.SafePath(directory);
   File.WriteAllBytes(Path.Combine(directory,"manifest.json"),candidate.ManifestBytes);File.WriteAllBytes(Path.Combine(directory,"manifest.sig"),candidate.Signature);File.WriteAllBytes(Path.Combine(directory,"candidate.exe"),bytes);UpdateSecurity.VerifyFile(Path.Combine(directory,"candidate.exe"),verified);
   string original=UpdateSecurity.HashFile(target);File.Copy(target,Path.Combine(directory,"helper.exe"));if(UpdateSecurity.HashFile(Path.Combine(directory,"helper.exe"))!=original)throw new InvalidDataException("Trusted helper copy could not be verified.");Atomic(Path.Combine(directory,"pending.json"),new UpdatePending{Target=target,OriginalHash=original,CurrentVersion=current,IncludeBeta=beta});return id;
  }
  public bool Launch(bool manual,Settings settings){
   if(!UpdateSecurity.Configured||String.IsNullOrEmpty(State.PendingId)||!manual&&(!State.Automatic||!settings.InstallUpdatesAutomatically))return false;
   string directory=PendingDirectory(),helper=Path.Combine(directory,"helper.exe");var pending=Read<UpdatePending>(Path.Combine(directory,"pending.json"));string target=Process.GetCurrentProcess().MainModule.FileName;UpdateSecurity.SafePath(target);UpdateSecurity.SafePath(helper);
   if(pending==null||!String.Equals(target,pending.Target,StringComparison.OrdinalIgnoreCase)||pending.CurrentVersion!=BetaDiagnostics.Version||UpdateSecurity.HashFile(target)!=pending.OriginalHash||UpdateSecurity.HashFile(helper)!=pending.OriginalHash)throw new InvalidDataException("Staged update no longer matches this installation.");
   var verified=UpdateSecurity.VerifyManifest(UpdateInstaller.ReadBounded(Path.Combine(directory,"manifest.json"),UpdateSecurity.ManifestLimit),UpdateInstaller.ReadBounded(Path.Combine(directory,"manifest.sig"),UpdateSecurity.SignatureLimit),DistributionConfig.Repository,DistributionConfig.UpdatePublicKeyXml,BetaDiagnostics.Version,settings.IncludePrereleases);UpdateSecurity.VerifyFile(Path.Combine(directory,"candidate.exe"),verified);
   // Carry the current, verified policy into helper dispatch, not staging history.
   pending.IncludeBeta=settings.IncludePrereleases;Atomic(Path.Combine(directory,"pending.json"),pending);
   // Only two locally generated numeric arguments; remote metadata never enters command text.
   var parent=Process.GetCurrentProcess();bool created;using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\ProgressPeekUpdateReady-"+State.PendingId+"-"+parent.Id.ToString(CultureInfo.InvariantCulture),out created)){if(!created)throw new InvalidDataException("Update handshake already exists.");Process.Start(new ProcessStartInfo(helper,"--update-helper "+parent.Id.ToString(CultureInfo.InvariantCulture)+" "+parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory});if(!ready.WaitOne(10000))throw new IOException("Update helper did not become ready.");return true;}
  }
 }
 public static class UpdateInstaller {
  public static Process ValidateOrigin(int pid,long ticks,string target,string originalHash,string helper){UpdateSecurity.SafePath(target);UpdateSecurity.SafePath(helper);if(Path.GetFileName(target)!=UpdateSecurity.ExecutableName||!Path.IsPathRooted(target)||!Path.IsPathRooted(helper))throw new InvalidDataException();var parent=Process.GetProcessById(pid);try{if(parent.StartTime.ToUniversalTime().Ticks!=ticks||!String.Equals(parent.MainModule.FileName,target,StringComparison.OrdinalIgnoreCase)||UpdateSecurity.HashFile(helper)!=originalHash||UpdateSecurity.HashFile(target)!=originalHash)throw new InvalidDataException();return parent;}catch{parent.Dispose();throw;}}
  public static bool TryScheduledLaunch(){if(!UpdateSecurity.Configured)return false;try{bool created;using(var gate=new Mutex(true,"Local\\TaskProgressOverlay",out created)){if(!created)return false;var store=new Storage();if(!store.Settings.CheckUpdatesAutomatically)return false;return new UpdateStaging(store.Folder).Launch(false,store.Settings);}}catch{return false;}}
  public static void ReplaceVerified(string staged,string target,string backup,VerifiedUpdate manifest){
   staged=Path.GetFullPath(staged);target=Path.GetFullPath(target);backup=Path.GetFullPath(backup);UpdateSecurity.SafePath(staged);UpdateSecurity.SafePath(target);UpdateSecurity.SafePath(backup);if(Path.GetFileName(target)!=UpdateSecurity.ExecutableName||backup!=target+".previous"||!File.Exists(target)||staged==target)throw new InvalidDataException("Unsupported replacement target.");
   // Replacement source is on the same volume/directory, and locked against writes while hashing.
   string swap=Path.Combine(Path.GetDirectoryName(target),".ProgressPeek-swap-"+Guid.NewGuid().ToString("N")+".tmp");try{using(var source=new FileStream(staged,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=new FileStream(swap,FileMode.CreateNew,FileAccess.Write,FileShare.None)){source.CopyTo(output);}UpdateSecurity.VerifyFile(swap,manifest);File.Replace(swap,target,backup);}finally{if(File.Exists(swap))File.Delete(swap);}
  }
  public static bool HandleHelper(string[] args){
   if(args.Length==0||args[0]!="--update-helper")return false;
   try{
    if(args.Length!=3||!UpdateSecurity.Configured)throw new InvalidDataException();int pid;long ticks;if(!Int32.TryParse(args[1],out pid)||!Int64.TryParse(args[2],out ticks))throw new InvalidDataException();string own=Assembly.GetExecutingAssembly().Location,directory=Path.GetDirectoryName(own),root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ProgressPeek","updates");Guid id;
    if(Path.GetFileName(own)!="helper.exe"||!Guid.TryParseExact(Path.GetFileName(directory),"N",out id)||!String.Equals(Path.GetDirectoryName(directory),root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();UpdateSecurity.SafePath(directory);var pending=UpdateStaging.Read<UpdatePending>(Path.Combine(directory,"pending.json"));if(pending==null||pending.CurrentVersion!=BetaDiagnostics.Version)throw new InvalidDataException();UpdateSecurity.SafePath(pending.Target);
    using(var parent=ValidateOrigin(pid,ticks,pending.Target,pending.OriginalHash,own)){using(var ready=EventWaitHandle.OpenExisting("Local\\ProgressPeekUpdateReady-"+id.ToString("N")+"-"+pid.ToString(CultureInfo.InvariantCulture)))ready.Set();if(!parent.WaitForExit(60000))throw new TimeoutException();}
    bool created;using(var mutex=new Mutex(true,"Local\\TaskProgressOverlay",out created)){if(!created)throw new IOException("Another instance is running.");try{
     UpdateSecurity.SafePath(pending.Target);if(UpdateSecurity.HashFile(pending.Target)!=pending.OriginalHash)throw new InvalidDataException();var manifest=UpdateSecurity.VerifyManifest(ReadBounded(Path.Combine(directory,"manifest.json"),UpdateSecurity.ManifestLimit),ReadBounded(Path.Combine(directory,"manifest.sig"),UpdateSecurity.SignatureLimit),DistributionConfig.Repository,DistributionConfig.UpdatePublicKeyXml,BetaDiagnostics.Version,pending.IncludeBeta);ReplaceVerified(Path.Combine(directory,"candidate.exe"),pending.Target,pending.Target+".previous",manifest);
     try{var state=new UpdateStaging(Path.GetDirectoryName(root));if(state.State.PendingId==id.ToString("N")){state.State.PendingId="";state.State.Automatic=false;state.State.Status="Update installed; the previous executable is retained.";state.Save();}}catch{}
    }finally{mutex.ReleaseMutex();}}
    // Normal entry uses createdNew admission: dispose the handle before startup.
    try{Process.Start(new ProcessStartInfo(pending.Target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(pending.Target)});}catch{RestorePrevious(pending.Target);throw;}
   }catch{try{var state=new UpdateStaging(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ProgressPeek"));state.State.Automatic=false;state.State.Status="Update could not be applied or restarted. A retained backup may be available; use manual recovery or download.";state.Save();}catch{}Environment.ExitCode=1;}
   return true;
  }
  internal static byte[] ReadBounded(string path,int limit){UpdateSecurity.SafePath(path);using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(file.Length>limit)throw new InvalidDataException();byte[] bytes=new byte[(int)file.Length];int read=0,count;while(read<bytes.Length&&(count=file.Read(bytes,read,bytes.Length-read))>0)read+=count;if(read!=bytes.Length)throw new InvalidDataException();return bytes;}}
  public static void RestorePrevious(string target){UpdateSecurity.SafePath(target);UpdateSecurity.SafePath(target+".previous");if(File.Exists(target+".previous"))File.Replace(target+".previous",target,target+".failed");}
 }
}
