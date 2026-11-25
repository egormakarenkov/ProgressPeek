using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ProgressPeek;

class UpdateTests {
 static int failures,checks;
 static void Check(bool ok,string name){checks++;if(!ok){failures++;Console.WriteLine("FAIL: "+name);}}
 static bool Reject(Action action){try{action();return false;}catch{return true;}}
 static byte[] Manifest(string version,string channel,string file,long size,string hash){return Encoding.UTF8.GetBytes("{\"app\":\"ProgressPeek\",\"repository\":\"fixture/publisher\",\"version\":\""+version+"\",\"channel\":\""+channel+"\",\"filename\":\""+file+"\",\"size\":"+size+",\"sha256\":\""+hash+"\"}");}
 [STAThread]static void Main(string[] args){if(args.Length==1&&args[0]=="--fixture-wait"){Thread.Sleep(1500);return;}Run(Check);Console.WriteLine(checks+" update checks; "+failures+" failures");Environment.ExitCode=failures>0?1:0;}
 public static void Run(Action<bool,string> assert){
  var type=typeof(Storage).Assembly.GetType("ProgressPeek.UpdateSecurity");assert(type!=null,"signed updater security boundary exists");if(type==null)return;
  RunPresent(assert);
  RunNotificationOwnership(assert);
 }
 static void RunNotificationOwnership(Action<bool,string> assert){
  var type=typeof(Storage).Assembly.GetType("ProgressPeek.TrayNotifications");assert(type!=null,"tray notification actions have a shared owner");if(type==null)return;
  using(var icon=new System.Windows.Forms.NotifyIcon()){
   object router=Activator.CreateInstance(type,new object[]{icon});var show=type.GetMethod("Show");int updates=0;
   Action notifyUpdate=()=>show.Invoke(router,new object[]{5000,"Update","Available",System.Windows.Forms.ToolTipIcon.Info,new Action(()=>updates++)});
   Action click=()=>typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnBalloonTipClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(icon,null);
   Action close=()=>typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnBalloonTipClosed",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(icon,null);
   notifyUpdate();click();assert(updates==1,"update balloon invokes its Updates action");click();assert(updates==1,"balloon action is consumed by the first click");
   notifyUpdate();show.Invoke(router,new object[]{5000,"ProgressPeek","Response finished",System.Windows.Forms.ToolTipIcon.Info,null});click();assert(updates==1,"completion balloon replaces the update action");
   notifyUpdate();show.Invoke(router,new object[]{5000,"ProgressPeek shortcut","In use",System.Windows.Forms.ToolTipIcon.Warning,null});click();assert(updates==1,"shortcut balloon replaces the update action");
   notifyUpdate();close();click();assert(updates==1,"dismissed balloon cannot invoke a stale update action");
  }
 }
 static void RunPresent(Action<bool,string> assert){
  // Reflection keeps the RED compilation valid before the production boundary exists.
  var a=typeof(Storage).Assembly;var security=a.GetType("ProgressPeek.UpdateSecurity");var verify=security.GetMethod("VerifyManifest");var hash=security.GetMethod("Hash");
  Func<byte[],byte[],string,string,bool,object> validate=(bytes,sig,key,current,beta)=>verify.Invoke(null,new object[]{bytes,sig,"fixture/publisher",key,current,beta});
  Func<byte[],string> digest=b=>(string)hash.Invoke(null,new object[]{b});
  using(var key=new RSACryptoServiceProvider(2048))using(var wrong=new RSACryptoServiceProvider(2048)){
   key.PersistKeyInCsp=false;wrong.PersistKeyInCsp=false;byte[] payload=Encoding.UTF8.GetBytes("fixture executable bytes");string sha=digest(payload);
   byte[] bytes=Manifest("0.4.0-beta.1","beta","ProgressPeek.exe",payload.Length,sha);byte[] sig=key.SignData(bytes,"SHA256");string pub=key.ToXmlString(false);
   object good=validate(bytes,sig,pub,"0.2.2-beta",true);assert(good!=null,"RSA SHA256 signed candidate accepts newer beta including current beta suffix");
   assert(Reject(()=>validate(bytes,sig,wrong.ToXmlString(false),"0.2.2-beta",true)),"wrong publisher key rejected");
   var changed=(byte[])bytes.Clone();changed[10]^=1;assert(Reject(()=>validate(changed,sig,pub,"0.2.2-beta",true)),"exact manifest byte tampering rejected");
   assert(Reject(()=>validate(bytes,sig,pub,"0.4.1",true)),"downgrade rejected");assert(Reject(()=>validate(bytes,sig,pub,"0.2.2-beta",false)),"beta excluded on stable channel");
   foreach(string file in new[]{"../ProgressPeek.exe","other.exe","C:\\x.exe"}){var bad=Manifest("0.3.0","stable",file,payload.Length,sha);assert(Reject(()=>validate(bad,key.SignData(bad,"SHA256"),pub,"0.2.2-beta",true)),"unsafe or wrong fixed executable name rejected");}
   var duplicate=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"app\":\"ProgressPeek\",","\"app\":\"wrong\",\"app\":\"ProgressPeek\","));assert(Reject(()=>validate(duplicate,key.SignData(duplicate,"SHA256"),pub,"0.2.2-beta",true)),"duplicate manifest keys rejected");
   var wrongRepo=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("fixture/publisher","other/publisher"));assert(Reject(()=>validate(wrongRepo,key.SignData(wrongRepo,"SHA256"),pub,"0.2.2-beta",true)),"signed repository mismatch rejected");
   string temp=Path.Combine(Path.GetTempPath(),"ProgressPeek-update-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   try{
    var install=a.GetType("ProgressPeek.UpdateInstaller");var replace=install.GetMethod("ReplaceVerified");string target=Path.Combine(temp,"ProgressPeek.exe"),staged=Path.Combine(temp,"candidate.exe"),backup=target+".previous";File.WriteAllText(target,"previous fixture");File.WriteAllBytes(staged,payload);
    Action swap=()=>replace.Invoke(null,new object[]{staged,target,backup,good});swap();assert(File.ReadAllBytes(target).Length==payload.Length&&File.ReadAllText(backup)=="previous fixture","actual atomic replacement keeps previous executable");
    File.WriteAllText(staged,"corrupt");assert(Reject(swap)&&File.ReadAllBytes(target).Length==payload.Length,"corrupt staged payload cannot replace installed bytes");
    File.WriteAllBytes(staged,payload);using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.None))assert(Reject(swap),"locked target fails safely");assert(File.ReadAllBytes(target).Length==payload.Length,"locked target leaves installed bytes intact");
    var restore=install.GetMethod("RestorePrevious");restore.Invoke(null,new object[]{target});assert(File.ReadAllText(target)=="previous fixture"&&File.ReadAllBytes(target+".failed").Length==payload.Length,"recovery restores previous executable and retains failed candidate");
    var origin=install.GetMethod("ValidateOrigin");assert(origin!=null,"helper parent identity validation exists");
    if(origin!=null){string fixture=Path.Combine(temp,"origin");Directory.CreateDirectory(fixture);string parentExe=Path.Combine(fixture,"ProgressPeek.exe"),helperExe=Path.Combine(fixture,"helper.exe");File.Copy(a.Location,parentExe);File.Copy(a.Location,helperExe);string original=(string)security.GetMethod("HashFile").Invoke(null,new object[]{parentExe});using(var parent=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(parentExe,"--fixture-wait"){UseShellExecute=false,CreateNoWindow=true})){long ticks=parent.StartTime.ToUniversalTime().Ticks;assert(!Reject(()=>origin.Invoke(null,new object[]{parent.Id,ticks,parentExe,original,helperExe})),"actual harmless child process satisfies helper origin identity");assert(Reject(()=>origin.Invoke(null,new object[]{parent.Id,ticks+1,parentExe,original,helperExe})),"PID reuse/start time mismatch rejected");assert(Reject(()=>origin.Invoke(null,new object[]{parent.Id,ticks,target,original,helperExe})),"unrelated destination cannot be nominated by helper");parent.WaitForExit(5000);}}
    var writePackage=a.GetType("ProgressPeek.UpdateStaging").GetMethod("WritePackage",BindingFlags.Static|BindingFlags.NonPublic);assert(writePackage!=null,"real staging package writer exists");if(writePackage!=null){string root=Path.Combine(temp,"packages");Directory.CreateDirectory(root);var candidate=new UpdateCandidate{Manifest=(VerifiedUpdate)good,ManifestBytes=bytes,Signature=sig};string id=(string)writePackage.Invoke(null,new object[]{root,candidate,payload,target,(VerifiedUpdate)good,"0.2.2-beta",true});string package=Path.Combine(root,id);assert(File.ReadAllBytes(Path.Combine(package,"candidate.exe")).Length==payload.Length&&File.ReadAllText(Path.Combine(package,"helper.exe"))=="previous fixture","staging stores verified candidate and clones current trusted target");assert(File.Exists(Path.Combine(package,"manifest.json"))&&File.Exists(Path.Combine(package,"manifest.sig"))&&File.Exists(Path.Combine(package,"pending.json")),"staging retains exact signed metadata and atomically records pending identity");assert(Reject(()=>writePackage.Invoke(null,new object[]{root,candidate,new byte[]{1},target,(VerifiedUpdate)good,"0.2.2-beta",true})),"staging rejects corrupt bytes before writing package");}
    var store=new Storage(Path.Combine(temp,"state"));store.Settings.DailyChecks=37;var checkAuto=typeof(Settings).GetField("CheckUpdatesAutomatically");var installAuto=typeof(Settings).GetField("InstallUpdatesAutomatically");var beta=typeof(Settings).GetField("IncludePrereleases");assert(checkAuto!=null&&installAuto!=null&&beta!=null,"update preferences exist");if(checkAuto!=null){assert((bool)checkAuto.GetValue(store.Settings)&&!(bool)installAuto.GetValue(store.Settings)&&!(bool)beta.GetValue(store.Settings),"safe update defaults");checkAuto.SetValue(store.Settings,false);installAuto.SetValue(store.Settings,true);store.SaveSettings();var loaded=new Storage(store.Folder);assert(!(bool)checkAuto.GetValue(loaded.Settings)&&(bool)installAuto.GetValue(loaded.Settings)&&loaded.Settings.DailyChecks==37,"update preferences persist and preserve existing settings");}
    var urls=security.GetMethod("ValidateUrl");assert(Reject(()=>urls.Invoke(null,new object[]{"http://github.com/fixture/publisher/releases/download/v0.3.0/ProgressPeek.exe","fixture/publisher","v0.3.0","ProgressPeek.exe",false})),"HTTP update rejected");assert(Reject(()=>urls.Invoke(null,new object[]{"https://github.com.evil/fixture/publisher/releases/download/v0.3.0/ProgressPeek.exe","fixture/publisher","v0.3.0","ProgressPeek.exe",false})),"lookalike update host rejected");
    var transport=new NeverTransport();var client=new UpdateClient("","","0.2.2-beta",transport);assert(client.Discover(true,CancellationToken.None)==null&&transport.Requests==0,"unconfigured updater makes no endpoint requests");
    var invalidClient=new UpdateClient("fixture/publisher","invalid-key","0.2.2-beta",transport);assert(!Reject(()=>invalidClient.Discover(true,CancellationToken.None))&&transport.Requests==0,"invalid compiled key makes no endpoint requests");
    var fixedTransport=new FixtureTransport(bytes,sig,payload);var configured=new UpdateClient("fixture/publisher",pub,"0.2.2-beta",fixedTransport);var found=configured.Discover(true,CancellationToken.None);assert(found!=null&&found.Manifest.Version=="0.4.0-beta.1","release discovery verifies tag channel identity and detached signature");assert(configured.Download(found,CancellationToken.None).Length==payload.Length,"download enforces signed size and digest through injected network boundary");fixedTransport.Payload=new byte[]{1};assert(Reject(()=>configured.Download(found,CancellationToken.None)),"network payload size mismatch rejected");fixedTransport.Payload=(byte[])payload.Clone();fixedTransport.Payload[0]^=1;assert(Reject(()=>configured.Download(found,CancellationToken.None)),"network payload hash mismatch rejected");
   }finally{Directory.Delete(temp,true);}
  }
 }
 sealed class NeverTransport:IUpdateTransport {public int Requests;public byte[] Read(Uri uri,int limit,CancellationToken token,Func<Uri,bool> redirects){Requests++;throw new Exception("An unconfigured updater attempted a request");}}
 sealed class FixtureTransport:IUpdateTransport {readonly byte[] manifest,signature;public byte[] Payload;public FixtureTransport(byte[] m,byte[] s,byte[] p){manifest=m;signature=s;Payload=p;}public byte[] Read(Uri uri,int limit,CancellationToken token,Func<Uri,bool> redirects){token.ThrowIfCancellationRequested();if(uri.Host=="api.github.com"){string url="https://github.com/fixture/publisher/releases/download/v0.4.0-beta.1/";return Encoding.UTF8.GetBytes("[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v0.4.0-beta.1\",\"body\":\"fixture plain text notes\",\"assets\":[{\"name\":\"ProgressPeek.update.json\",\"browser_download_url\":\""+url+"ProgressPeek.update.json\"},{\"name\":\"ProgressPeek.update.sig\",\"browser_download_url\":\""+url+"ProgressPeek.update.sig\"},{\"name\":\"ProgressPeek.exe\",\"browser_download_url\":\""+url+"ProgressPeek.exe\"}]}]");}if(uri.AbsolutePath.EndsWith(".json"))return manifest;if(uri.AbsolutePath.EndsWith(".sig"))return signature;return Payload;}}
}
