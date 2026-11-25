using System;
using System.Reflection;
using System.Threading;
using ProgressPeek;

public static class DiagnosticTests {
 public static void Run(Action<bool,string> check) {
  const string secret="private-prompt user@example.test sk-sensitive C:\\Users\\Secret\\auth.json https://private.test/token";
  var settings=new Settings();
  foreach(var field in typeof(Settings).GetFields())if(field.FieldType==typeof(string))field.SetValue(settings,secret);
  var snapshot=new Snapshot{Handle=new IntPtr(8675309),NodeCount=55,Generating=true,Truncated=true,FinalVisible=true};
  foreach(var field in typeof(Snapshot).GetFields())if(field.FieldType==typeof(string))field.SetValue(snapshot,secret);
  string report=BetaDiagnostics.Report(settings,snapshot,secret);
  check(!report.Contains(secret)&&!report.Contains("private-prompt")&&!report.Contains("user@example")&&!report.Contains("sk-sensitive")&&!report.Contains("Secret")&&!report.Contains("private.test")&&!report.Contains("8675309"),"diagnostic report allowlist excludes every input string and window handle");
  check(report.Contains("App version: "+BetaDiagnostics.Version)&&report.Contains("Visible nodes: 55")&&report.Contains("Last failure: other"),"diagnostic report retains useful safe metadata");
  snapshot.NodeCount=Int32.MaxValue;
  check(BetaDiagnostics.Report(null,snapshot,null).Contains("Visible nodes: 10000"),"diagnostic node count bounded and null settings accepted");
  check(BetaDiagnostics.Report(null,null,null).Contains("Snapshot available: no"),"diagnostic report handles no scan");
  check(BetaDiagnostics.FailureCategory("Codex CLI not found")=="cli-missing"&&BetaDiagnostics.FailureCategory("Codex CLI not found "+secret)=="other","failure category exact allowlist rejects injected suffix");
  check(BetaDiagnostics.FailureCategory("Accessibility unavailable")=="accessibility-unavailable"&&BetaDiagnostics.FailureCategory("")=="none","safe failure categories are explicit");
  var parser=new LoginStatusAccumulator();
  parser.Accept("Logged in using Ch");parser.Accept("atGPT");
  check(parser.PlanLogin&&!parser.Overflow,"login parser recognizes status split across chunks");
  var api=new LoginStatusAccumulator();api.Accept("Logged in using an API key: "+secret);
  check(!api.PlanLogin,"login parser never treats API key login as plan login");
  parser.Accept(new string('x',SetupChecker.MaximumOutputCharacters+1));
  check(parser.Overflow,"login output has a hard character bound");
  using(var stop=new CancellationTokenSource()) {
   stop.Cancel();bool cancelled=false;
   try{SetupChecker.Check(null,false,stop.Token);}catch(OperationCanceledException){cancelled=true;}
   check(cancelled,"pre-cancelled setup never discovers or launches a CLI");
  }
  var demo=SetupChecker.Check(null,true,CancellationToken.None);
  check(demo.State==SetupCheckState.Demo&&!demo.CliFound,"demo setup does not discover or launch a CLI");
  var failure=SetupChecker.Check(null,false,CancellationToken.None);
  check(failure.State==SetupCheckState.Failed&&!failure.CliFound,"setup discovery exceptions become a safe failure status");
  check(!BetaDiagnostics.Report(settings,snapshot,secret,new SetupCheckResult{State=(SetupCheckState)999,CliFound=true}).Contains("999"),"unknown setup state cannot leak arbitrary values");
  foreach(string accent in new[]{"Blue","Green","Purple","Amber"}){settings.Accent=accent;check(BetaDiagnostics.Report(settings,null,null).Contains("Accent: "+accent+Environment.NewLine),"diagnostic report preserves supported accent "+accent);}
 }
 public static int Main(){int failed=0;Run((ok,name)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failed++;});return failed==0?0:1;}
}
