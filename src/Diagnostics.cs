using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProgressPeek {
 public enum SetupCheckState { NotChecked, Demo, MissingCli, PlanLogin, OtherLogin, TimedOut, Failed, OutputLimit }
 public sealed class SetupCheckResult {public bool CliFound;public SetupCheckState State;}

 // Retain only a short matching tail; never return, persist, or display command output.
 public sealed class LoginStatusAccumulator {
  readonly object sync=new object();string tail="";int count;bool plan,overflow;
  public bool PlanLogin {get{lock(sync)return plan;}}
  public bool Overflow {get{lock(sync)return overflow;}}
  public void Accept(string chunk){lock(sync){if(overflow||chunk==null)return;if(chunk.Length>SetupChecker.MaximumOutputCharacters-count){overflow=true;tail="";return;}count+=chunk.Length;string text=tail+chunk;plan|=CliEstimator.IsPlanLogin(text);tail=text.Substring(Math.Max(0,text.Length-48));}}
  public void Clear(){lock(sync)tail="";}
 }
 public static class SetupChecker {
  public const int TimeoutMilliseconds=5000,MaximumOutputCharacters=32768;
  static void Kill(Process process){try{if(!process.HasExited)process.Kill();}catch{}}
  static Task Drain(StreamReader reader,LoginStatusAccumulator status){return Task.Factory.StartNew(()=>{var buffer=new char[256];try{int n;while((n=reader.Read(buffer,0,buffer.Length))>0){status.Accept(new string(buffer,0,n));if(status.Overflow)break;}}catch{}finally{Array.Clear(buffer,0,buffer.Length);status.Clear();}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
  public static SetupCheckResult Check(Storage storage,bool demo,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();if(demo)return new SetupCheckResult{State=SetupCheckState.Demo};
   var result=new SetupCheckResult();
   try {
    string cli=new CliEstimator(storage).FindCli();cancel.ThrowIfCancellationRequested();
    if(String.IsNullOrEmpty(cli)){result.State=SetupCheckState.MissingCli;return result;}result.CliFound=true;
    var info=new ProcessStartInfo(cli,"login status"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    info.EnvironmentVariables.Remove("OPENAI_API_KEY");info.EnvironmentVariables.Remove("CODEX_API_KEY");
    using(var process=new Process{StartInfo=info}){
     cancel.ThrowIfCancellationRequested();process.Start();process.StandardInput.Close();
     using(cancel.Register(()=>Kill(process))){
      var stdout=new LoginStatusAccumulator();var stderr=new LoginStatusAccumulator();
      Task a=Drain(process.StandardOutput,stdout),b=Drain(process.StandardError,stderr);var clock=Stopwatch.StartNew();
      try {
       while(!process.WaitForExit(50)){
        cancel.ThrowIfCancellationRequested();
        if(stdout.Overflow||stderr.Overflow){result.State=SetupCheckState.OutputLimit;return result;}
        if(clock.ElapsedMilliseconds>=TimeoutMilliseconds){result.State=SetupCheckState.TimedOut;return result;}
       }
       cancel.ThrowIfCancellationRequested();
       if(!Task.WaitAll(new[]{a,b},250)){result.State=SetupCheckState.Failed;return result;}
       cancel.ThrowIfCancellationRequested();
       result.State=stdout.Overflow||stderr.Overflow?SetupCheckState.OutputLimit:process.ExitCode==0&&(stdout.PlanLogin||stderr.PlanLogin)?SetupCheckState.PlanLogin:SetupCheckState.OtherLogin;
       return result;
      }finally{Kill(process);stdout.Clear();stderr.Clear();}
     }
    }
   }catch(OperationCanceledException){throw;}catch{cancel.ThrowIfCancellationRequested();result.State=SetupCheckState.Failed;return result;}
  }
 }

 public static class BetaDiagnostics {
  public const string Version="0.4.0-beta";
  static string Yes(bool value){return value?"yes":"no";}
  static string Choice(string value,params string[] allowed){foreach(string item in allowed)if(value==item)return item;return "other";}
  static int Bound(int value,int maximum){return Math.Max(0,Math.Min(maximum,value));}
  public static bool Readable(Snapshot snapshot){return snapshot!=null&&snapshot.Handle!=IntPtr.Zero&&String.IsNullOrEmpty(snapshot.Error)&&!String.IsNullOrWhiteSpace(snapshot.Prompt);}
  public static string FailureCategory(string error){switch(error){
   case null:case "":return "none";
   case "Codex CLI not found":return "cli-missing";
   case "Sign-in check timed out":return "login-timeout";
   case "Codex must be signed in using ChatGPT":return "plan-login-required";
   case "Accessibility unavailable":return "accessibility-unavailable";
   case "Chat layout changed":return "chat-layout-changed";
   case "Estimate timed out; no automatic retry.":case "Estimate timed out; no automatic retry":return "estimate-timeout";
   case "Estimator returned invalid estimate data.":case "Estimator returned malformed estimate data. Refresh to try again.":case "Estimator returned an invalid percentage range. Refresh to try again.":case "Estimator returned invalid step values. Refresh to try again.":case "Estimator returned invalid summary fields. Refresh to try again.":return "invalid-estimate";
   case "AI estimates disabled; current check cancelled.":return "estimates-disabled";
   default:return "other";
  }}
  public static string SetupCategory(SetupCheckState state){switch(state){case SetupCheckState.NotChecked:return "not-checked";case SetupCheckState.Demo:return "demo-synthetic";case SetupCheckState.MissingCli:return "cli-missing";case SetupCheckState.PlanLogin:return "plan-login-detected";case SetupCheckState.OtherLogin:return "plan-login-not-confirmed";case SetupCheckState.TimedOut:return "timed-out";case SetupCheckState.OutputLimit:return "output-limit";default:return "check-failed";}}
  public static string Report(Settings settings,Snapshot snapshot,string error){return Report(settings,snapshot,error,null);}
  public static string Report(Settings settings,Snapshot snapshot,string error,SetupCheckResult setup){
   var s=settings??new Settings();var r=new StringBuilder();
   r.AppendLine("ProgressPeek beta diagnostic report");r.AppendLine("App version: "+Version);
   r.AppendLine("OS version: "+Environment.OSVersion.Version);r.AppendLine("Runtime version: "+Environment.Version);r.AppendLine("Process bitness: "+(Environment.Is64BitProcess?"64":"32"));r.AppendLine("OS bitness: "+(Environment.Is64BitOperatingSystem?"64":"32"));
   r.AppendLine("Estimates enabled: "+Yes(s.Enabled));r.AppendLine("Manual only: "+Yes(s.ManualOnly));r.AppendLine("Paused: "+Yes(s.Paused));r.AppendLine("Refresh seconds: "+Bound(s.AutoIntervalSeconds,3600));
   r.AppendLine("Daily limit enabled: "+Yes(s.EnforceDailyLimit));r.AppendLine("Daily limit: "+Bound(s.DailyChecks,100));r.AppendLine("Task limit enabled: "+Yes(s.EnforceTaskLimit));r.AppendLine("Task limit: "+Bound(s.TaskChecks,100));
   r.AppendLine("Account usage visible: "+Yes(s.ShowUsage));r.AppendLine("Click through: "+Yes(s.ClickThrough));r.AppendLine("Size locked: "+Yes(s.LockSize));r.AppendLine("Position locked: "+Yes(s.LockPosition));r.AppendLine("Edge snapping: "+Yes(s.SnapEdges));r.AppendLine("Hotkey enabled: "+Yes(s.HotkeyEnabled));
   r.AppendLine("Accent: "+Choice(s.Accent,"Blue","Green","Purple","Amber"));r.AppendLine("Text scale: "+(Double.IsNaN(s.TextScale)||Double.IsInfinity(s.TextScale)?"invalid":Math.Max(.75,Math.Min(2,s.TextScale)).ToString("0.00",CultureInfo.InvariantCulture)));
   r.AppendLine("Reduced motion: "+Yes(s.ReducedMotion));r.AppendLine("Freshness visible: "+Yes(s.ShowFreshness));r.AppendLine("Experimental ETA: "+Yes(s.ShowEta));r.AppendLine("Finish behavior: "+Choice(s.FinishBehavior,"Keep","Collapse","Hide"));r.AppendLine("Finish delay seconds: "+Bound(s.FinishDelaySeconds,3600));r.AppendLine("Finish notification: "+Yes(s.NotifyFinished));r.AppendLine("Finish sound: "+Yes(s.SoundFinished));
   r.AppendLine("Snapshot available: "+Yes(snapshot!=null));r.AppendLine("App detected in last scan: "+Yes(snapshot!=null&&snapshot.Handle!=IntPtr.Zero));r.AppendLine("Chat readable in last scan: "+Yes(Readable(snapshot)));r.AppendLine("Visible nodes: "+(snapshot==null?0:Bound(snapshot.NodeCount,10000)));
   r.AppendLine("Generating: "+Yes(snapshot!=null&&snapshot.Generating));r.AppendLine("Scan truncated: "+Yes(snapshot!=null&&snapshot.Truncated));r.AppendLine("Final marker visible: "+Yes(snapshot!=null&&snapshot.FinalVisible));r.AppendLine("Scan failure: "+FailureCategory(snapshot==null?null:snapshot.Error));r.AppendLine("Last failure: "+FailureCategory(error));
   r.AppendLine("CLI found: "+(setup==null||setup.State==SetupCheckState.Demo?"not-checked":Yes(setup.CliFound)));r.AppendLine("Login status: "+SetupCategory(setup==null?SetupCheckState.NotChecked:setup.State));
   r.AppendLine("Scope: local metadata only; no chat text, titles, identities, handles, account details, paths, or raw errors.");
   return r.ToString();
  }
 }
}
