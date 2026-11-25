using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
namespace ProgressPeek {
 public class Settings {
  public bool SetupCompleted=false;
  public bool Enabled=true,ClickThrough=false,EnforceDailyLimit=false,EnforceTaskLimit=false,ShowUsage=true,LockSize=false,HotkeyEnabled=true;public int DailyChecks=12,TaskChecks=3,AutoIntervalSeconds=30,LiveVersion=0;public double OffsetX=-350,OffsetY=430,WidgetWidth=320,WidgetHeight=0;public string CliPath="",Hotkey="Ctrl+Alt+P";
  public string Accent="Blue",FinishBehavior="Keep",TokenDisplay="Task";public double TextScale=1;
  public bool ReducedMotion=false,LockPosition=false,SnapEdges=false,NotifyFinished=false,SoundFinished=false,ShowFreshness=true,ManualOnly=false,Paused=false,ShowEta=false;
  public int FinishDelaySeconds=10;
  public bool AdaptiveTiming=true;
  public bool CheckUpdatesAutomatically=true,InstallUpdatesAutomatically=false,IncludePrereleases=false;
 }
 public class LedgerEntry {public int Calls,Failures;public DateTime Last,RetryAfter;public string Hash,Outcome;}
 public class Ledger {public string Day;public int DailyCalls;public Dictionary<string,LedgerEntry> Tasks=new Dictionary<string,LedgerEntry>();}
 public class Storage {
  public static string MigrationStatus="";
  public readonly string Folder;public Settings Settings;public Ledger Ledger;private object sync=new object();
  public Storage():this(DefaultFolder()){}
  static string DefaultFolder(){string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),folder=Path.Combine(local,"ProgressPeek");MigrationStatus=StateMigration.CopyOnce(Path.Combine(local,"TaskProgress"),folder);return folder;}
  public Storage(string folder){Folder=folder;Directory.CreateDirectory(Folder);Settings=Read<Settings>("settings.json")??new Settings();Ledger=Read<Ledger>("checks.json")??new Ledger();foreach(var entry in Ledger.Tasks.Values)if(entry.Outcome=="pending"){entry.Outcome="transient";entry.Failures++;entry.RetryAfter=entry.Last.AddSeconds(Math.Min(120,30*Math.Pow(2,entry.Failures-1)));}}
  T Read<T>(string name) where T:class {try{return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(Path.Combine(Folder,name)));}catch{return null;}}
  void Write(string name,object value){string path=Path.Combine(Folder,name),temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public void SaveSettings(){lock(sync)Write("settings.json",Settings);}
  public void UpgradeLiveDefaults(){lock(sync){if(Settings.LiveVersion>=1)return;Settings.LiveVersion=1;Settings.AutoIntervalSeconds=30;Settings.EnforceTaskLimit=false;Write("settings.json",Settings);}}
  public bool Reserve(string task,string hash,out int count,out string reason){return ReserveCore(task,hash,false,DateTime.UtcNow,true,out count,out reason);}
  public bool Reserve(string task,string hash,bool manual,DateTime now,out int count,out string reason){return ReserveCore(task,hash,manual,now,false,out count,out reason);}
  public bool Reserve(string task,string hash,bool manual,DateTime now,int interval,out int count,out string reason){return ReserveCore(task,hash,manual,now,false,out count,out reason,interval);}
  public void CompleteReservation(string task,string hash,string outcome,DateTime now){lock(sync){LedgerEntry e;if(!Ledger.Tasks.TryGetValue(task,out e)||e.Hash!=hash)return;e.Outcome=outcome;if(outcome=="transient"){e.Failures++;e.RetryAfter=now.AddSeconds(Math.Min(120,30*Math.Pow(2,e.Failures-1)));}else e.RetryAfter=DateTime.MinValue;try{Write("checks.json",Ledger);}catch{}}}
  bool ReserveCore(string task,string hash,bool manual,DateTime now,bool legacy,out int count,out string reason,int interval=0){lock(sync){count=0;reason="";var day=now.ToString("yyyy-MM-dd");if(Ledger.Day!=day){Ledger.Day=day;Ledger.DailyCalls=0;}LedgerEntry e;if(!Ledger.Tasks.TryGetValue(task,out e)){e=new LedgerEntry();Ledger.Tasks[task]=e;}
   count=e.Calls;if(!manual&&(legacy||Settings.EnforceTaskLimit)&&e.Calls>=Math.Max(1,Math.Min(100,Settings.TaskChecks))){reason="Task check limit reached";return false;}if(Settings.EnforceDailyLimit&&Ledger.DailyCalls>=Math.Max(1,Math.Min(100,Settings.DailyChecks))){reason="Daily check limit reached";return false;}if(!manual&&e.Hash==hash){if(e.Outcome!="transient"||e.Failures>=3){reason=e.Outcome=="permanent"?"Automatic retry stopped; refresh manually":e.Failures>=3?"Retry limit reached; refresh manually":"Evidence unchanged";return false;}if(now<e.RetryAfter){reason="Waiting to retry temporary failure";return false;}}if((now-e.Last).TotalSeconds<(manual?10:legacy?90:Math.Max(15,Math.Min(300,interval>0?interval:Settings.AutoIntervalSeconds)))){reason=manual?"Please wait a moment before refreshing again":legacy?"Waiting for new evidence":"Waiting for configured check interval";return false;}
   if(e.Hash!=hash||manual)e.Failures=0;e.Calls++;e.Hash=hash;e.Outcome=legacy?"success":"pending";e.Last=now;Ledger.DailyCalls++;count=e.Calls;
   try{Write("checks.json",Ledger);}catch{reason="Cannot save check reservation";return false;}return true;
  }}
 }
}
