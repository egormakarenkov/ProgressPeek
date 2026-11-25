using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ProgressPeek {
 public class UsageSnapshot {
  public DateTime FetchedUtc;
  public string Error;
  public List<UsageWindow> Windows = new List<UsageWindow>();
 }
 public class UsageWindow {
  public string Bucket, Label;
  // NaN means this reported window did not contain a valid usage percentage.
  public double Remaining = Double.NaN;
  public long? ResetsAt;
  public int DurationMinutes;
 }
 public static class UsageReader {
  const int Limit = 262144;
  const int TimeoutMilliseconds = 15000;
  static Dictionary<string,object> Object(object value) { return value as Dictionary<string,object>; }
  static object Get(Dictionary<string,object> value,string key) { object result; return value != null && value.TryGetValue(key,out result) ? result : null; }
  static bool Number(object value,out double number) {
   number = Double.NaN;
   if (!(value is int || value is long || value is double || value is decimal || value is float || value is short || value is byte)) return false;
   try { number = Convert.ToDouble(value,CultureInfo.InvariantCulture); return !Double.IsNaN(number) && !Double.IsInfinity(number); } catch { return false; }
  }
  static UsageSnapshot Failure(DateTime now,string message) { return new UsageSnapshot { FetchedUtc=now.ToUniversalTime(), Error=message }; }
  static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=Limit, RecursionLimit=32 }; }

  // Accepts a full JSON-RPC response envelope OR the account/rateLimits/read result object.
  // A present rateLimitsByLimitId map is authoritative, including an empty map.
  public static UsageSnapshot Parse(string json,DateTime now) {
   var snapshot = new UsageSnapshot { FetchedUtc=now.ToUniversalTime() };
   try {
    if (String.IsNullOrWhiteSpace(json) || json.Length>Limit) return Failure(now,"Account usage is unavailable.");
    var root = Object(Serializer().DeserializeObject(json));
    if (root==null || Get(root,"error")!=null) return Failure(now,"Account usage could not be read. Check sign-in and connection.");
    if (root.ContainsKey("result")) root=Object(root["result"]);
    if (root==null) return Failure(now,"Account usage is unavailable.");
    var buckets=Object(Get(root,"rateLimitsByLimitId"));
    if (buckets!=null) {
     var keys=new List<string>(buckets.Keys); keys.Sort(StringComparer.Ordinal);
     foreach (var key in keys) AddBucket(snapshot,key,Object(buckets[key]));
    } else {
     var legacy=Object(Get(root,"rateLimits"));
     if (legacy!=null) {
      string name=Get(legacy,"limitId") as string;
      AddBucket(snapshot,String.IsNullOrWhiteSpace(name)?"codex":name,legacy);
     }
    }
    bool usable=false; foreach (var window in snapshot.Windows) if (!Double.IsNaN(window.Remaining)) usable=true;
    if (!usable) snapshot.Error="Account usage is unavailable.";
    return snapshot;
   } catch { return Failure(now,"Account usage returned an unreadable response."); }
  }
  static void AddBucket(UsageSnapshot snapshot,string bucket,Dictionary<string,object> value) {
   if (value==null) return;
   int start=snapshot.Windows.Count;
   AddWindow(snapshot,bucket,"Primary",Object(Get(value,"primary")));
   AddWindow(snapshot,bucket,"Secondary",Object(Get(value,"secondary")));
   if (snapshot.Windows.Count-start==2 && snapshot.Windows[start].Label==snapshot.Windows[start+1].Label) {
    snapshot.Windows[start].Label+=" (primary)"; snapshot.Windows[start+1].Label+=" (secondary)";
   }
  }
  static void AddWindow(UsageSnapshot snapshot,string bucket,string fallback,Dictionary<string,object> value) {
   if (value==null) return;
   var window=new UsageWindow { Bucket=bucket, Label=fallback };
   double number;
   if (Number(Get(value,"usedPercent"),out number)) window.Remaining=100-Math.Max(0,Math.Min(100,number));
   if (Number(Get(value,"windowDurationMins"),out number) && number>0 && number<=Int32.MaxValue && Math.Floor(number)==number) window.DurationMinutes=(int)number;
   if (window.DurationMinutes==10080) window.Label="Weekly";
   else if (window.DurationMinutes==300) window.Label="5-hour";
   else if (window.DurationMinutes>0) window.Label=window.DurationMinutes%60==0 ? (window.DurationMinutes/60).ToString(CultureInfo.InvariantCulture)+"-hour" : window.DurationMinutes.ToString(CultureInfo.InvariantCulture)+"-minute";
   // Restrict to the Unix timestamp range representable by DateTime. Do not infer a reset.
   if (Number(Get(value,"resetsAt"),out number) && number>=0 && number<=253402300799d && Math.Floor(number)==number) window.ResetsAt=(long)number;
   snapshot.Windows.Add(window);
  }
  static ProcessStartInfo StartInfo(string cli,string arguments,string directory) {
   var info=new ProcessStartInfo(cli,arguments) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8, WorkingDirectory=directory };
   info.EnvironmentVariables.Remove("OPENAI_API_KEY"); info.EnvironmentVariables.Remove("CODEX_API_KEY");
   return info;
  }
  static void Kill(Process process) { try { if (process!=null && !process.HasExited) process.Kill(); } catch {} }
  // Fixed-size reads prevent an untrusted or broken CLI from growing a ReadLine buffer.
  sealed class Output {
   public readonly object Sync=new object();
   public string Initialize, Usage;
   public bool Failed;
   public void Read(StreamReader reader) {
    try {
     char[] buffer=new char[2048]; var line=new StringBuilder(); int total=0,count;
     while ((count=reader.Read(buffer,0,buffer.Length))>0) {
      total+=count; if(total>Limit*4) { lock(Sync) Failed=true; return; }
      for(int i=0;i<count;i++) {
       char c=buffer[i];
       if(c=='\n') { Accept(line.ToString()); line.Length=0; }
       else if(c!='\r') { if(line.Length>=Limit) { lock(Sync) Failed=true; return; } line.Append(c); }
      }
     }
     if(line.Length>0) Accept(line.ToString());
    } catch { lock(Sync) Failed=true; }
   }
   void Accept(string line) {
    try {
     var envelope=Object(Serializer().DeserializeObject(line)); double id;
     if(envelope==null || !Number(Get(envelope,"id"),out id)) return;
     lock(Sync) { if(id==1) Initialize=line; else if(id==2) Usage=line; }
    } catch { lock(Sync) Failed=true; }
   }
  }
  static void Drain(StreamReader reader) { try { char[] buffer=new char[2048]; while(reader.Read(buffer,0,buffer.Length)>0) {} } catch {} }
  static string Await(Output output,bool initialize,Process process,Stopwatch watch,CancellationToken cancel) {
   for(;;) {
    cancel.ThrowIfCancellationRequested();
    if(watch.ElapsedMilliseconds>=TimeoutMilliseconds) throw new TimeoutException();
    lock(output.Sync) {
     string response=initialize?output.Initialize:output.Usage;
     if(response!=null) return response;
     if(output.Failed) throw new InvalidOperationException();
    }
    if(process.HasExited) throw new InvalidOperationException();
    cancel.WaitHandle.WaitOne(25);
   }
  }
  // Read-only RPC only: initialize, initialized, account/rateLimits/read. No thread or turn is created.
  public static UsageSnapshot Read(Storage storage,CancellationToken cancel) {
   var watch=Stopwatch.StartNew(); Process process=null; Thread stdout=null,stderr=null;
   Timer deadline=null; CancellationTokenRegistration registration=default(CancellationTokenRegistration);
   try {
    cancel.ThrowIfCancellationRequested();
    string cli=new CliEstimator(storage).FindCli();
    if(cli==null) return Failure(DateTime.UtcNow,"Codex CLI not found. Check its path in Settings.");
    if(watch.ElapsedMilliseconds>=TimeoutMilliseconds) throw new TimeoutException();
    // ignore-user-config/ignore-rules belong to `exec`, not app-server. Only supported
    // global configuration overrides are used here; no conversation/rules are loaded.
    const string arguments="--disable plugins --disable apps --disable shell_tool --disable memories --disable multi_agent --disable hooks --disable plugin_hooks --disable skill_search --disable code_mode --disable code_mode_host --disable code_mode_only --disable view_image --disable image_generation --disable tool_search --disable tool_suggest --disable js_repl --disable workspace_dependencies --disable goals --disable recommended_plugins --disable request_permissions_tool --disable apply_patch_freeform --disable unified_exec -c features.skip_host_skill_discovery=true -c web_search=\"disabled\" -c analytics.enabled=false -c suppress_unstable_features_warning=true app-server --listen stdio://";
    process=new Process { StartInfo=StartInfo(cli,arguments,storage.Folder) }; process.Start();
    var ownedProcess=process;
    int remaining=(int)Math.Max(1,TimeoutMilliseconds-watch.ElapsedMilliseconds);
    deadline=new Timer(delegate { Kill(ownedProcess); },null,remaining,Timeout.Infinite);
    registration=cancel.Register(delegate { Kill(ownedProcess); });
    var output=new Output();
    stdout=new Thread(delegate() { try { output.Read(ownedProcess.StandardOutput); } catch { lock(output.Sync) output.Failed=true; } }) { IsBackground=true };
    stderr=new Thread(delegate() { try { Drain(ownedProcess.StandardError); } catch {} }) { IsBackground=true };
    stdout.Start(); stderr.Start();
    process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"task_progress\",\"title\":\"ProgressPeek\",\"version\":\"1.1\"}}}"); process.StandardInput.Flush();
    var initialized=Object(Serializer().DeserializeObject(Await(output,true,process,watch,cancel)));
    if(initialized==null || Get(initialized,"error")!=null || !initialized.ContainsKey("result")) throw new InvalidOperationException();
    cancel.ThrowIfCancellationRequested();
    process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
    process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}"); process.StandardInput.Flush();
    string result=Await(output,false,process,watch,cancel);
    cancel.ThrowIfCancellationRequested();
    return Parse(result,DateTime.UtcNow);
   } catch(OperationCanceledException) { throw; }
   catch(TimeoutException) { return Failure(DateTime.UtcNow,"Account usage check timed out."); }
   catch { cancel.ThrowIfCancellationRequested(); return Failure(DateTime.UtcNow,watch.ElapsedMilliseconds>=TimeoutMilliseconds?"Account usage check timed out.":"Account usage could not be read. Check ChatGPT sign-in and connection."); }
   finally {
    if(deadline!=null) deadline.Dispose(); registration.Dispose(); Kill(process);
    if(stdout!=null && stdout.IsAlive) stdout.Join(100);
    if(stderr!=null && stderr.IsAlive) stderr.Join(100);
    if(process!=null) process.Dispose();
   }
  }
 }
}
