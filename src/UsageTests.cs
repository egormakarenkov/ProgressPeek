using System;
using System.Threading;
using ProgressPeek;

public static class UsageTests {
 static int checks;
 static void Check(bool condition,string name) { checks++; if(!condition) throw new Exception(name); }
 static readonly DateTime Now=new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc);
 static UsageSnapshot Parse(string value) { return UsageReader.Parse(value,Now); }
 public static int Main(string[] args) {
  if(args.Length==1 && args[0]=="--live") {
   var result=UsageReader.Read(new Storage(),CancellationToken.None);
   if(result.Error!=null) { Console.WriteLine(result.Error); return 1; }
   foreach(var window in result.Windows) Console.WriteLine(window.Bucket+" "+window.Label+": "+window.Remaining+"% remaining");
   return 0;
  }
  try {
   var weekly=Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":7,\"windowDurationMins\":10080,\"resetsAt\":1791450743},\"secondary\":null}}}");
   Check(weekly.Error==null && weekly.Windows.Count==1,"weekly primary and absent secondary");
   Check(weekly.Windows[0].Remaining==93 && weekly.Windows[0].Label=="Weekly" && weekly.Windows[0].ResetsAt==1791450743 && weekly.FetchedUtc==Now,"weekly metadata");
   var both=Parse("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":12.5,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":30,\"windowDurationMins\":10080}}}}");
   Check(both.Windows.Count==2 && both.Windows[0].Label=="5-hour" && both.Windows[0].Remaining==87.5 && both.Windows[1].Remaining==70,"legacy and envelope");
   var multiple=Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":20,\"windowDurationMins\":300}},\"review\":{\"secondary\":{\"usedPercent\":35,\"windowDurationMins\":10080}}},\"rateLimits\":{\"primary\":{\"usedPercent\":99}}}");
   Check(multiple.Windows.Count==2 && multiple.Windows[0].Bucket=="codex" && multiple.Windows[1].Bucket=="review" && multiple.Windows[1].Label=="Weekly","map preferred and buckets preserved");
   var unavailable=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":null,\"windowDurationMins\":null,\"resetsAt\":null},\"secondary\":{\"usedPercent\":\"12\",\"windowDurationMins\":\"300\",\"resetsAt\":\"12\"}}}");
   Check(unavailable.Error!=null && unavailable.Windows.Count==2 && Double.IsNaN(unavailable.Windows[0].Remaining) && Double.IsNaN(unavailable.Windows[1].Remaining),"missing and non-numeric usage unavailable");
   Check(unavailable.Windows[0].DurationMinutes==0 && unavailable.Windows[1].DurationMinutes==0 && !unavailable.Windows[1].ResetsAt.HasValue,"malformed metadata not coerced");
   var clamped=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":-12,\"windowDurationMins\":60,\"resetsAt\":-1},\"secondary\":{\"usedPercent\":140,\"windowDurationMins\":90,\"resetsAt\":999999999999}}}");
   Check(clamped.Windows[0].Remaining==100 && clamped.Windows[1].Remaining==0 && clamped.Windows[0].Label=="1-hour" && clamped.Windows[1].Label=="90-minute","clamp and arbitrary durations");
   Check(!clamped.Windows[0].ResetsAt.HasValue && !clamped.Windows[1].ResetsAt.HasValue,"invalid reset range");
   var repeated=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":0,\"windowDurationMins\":300}}}");
   Check(repeated.Windows[0].Label!=repeated.Windows[1].Label,"same duration windows distinguished");
   var partial=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":true},\"secondary\":{\"usedPercent\":7}}}");
   Check(partial.Error==null && Double.IsNaN(partial.Windows[0].Remaining) && partial.Windows[1].Remaining==93,"partial valid data retained");
   var expired=Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":50,\"resetsAt\":1}}}");
   Check(expired.Windows[0].Remaining==50 && expired.Windows[0].ResetsAt==1,"expired reset retained for UI stale handling");
   Check(Parse("{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":1}}}").Windows.Count==0,"empty authoritative map");
   Check(Parse("{\"rateLimitsByLimitId\":null,\"rateLimits\":{\"primary\":{\"usedPercent\":1}}}").Windows.Count==1,"null map legacy fallback");
   Check(Parse("{\"error\":{\"message\":\"secret diagnostic must not escape\"}}").Error.IndexOf("secret",StringComparison.Ordinal)<0,"safe RPC error");
   foreach(string invalid in new string[]{null,"","broken","[]","null","{\"result\":null}","{\"rateLimits\":{\"primary\":null,\"secondary\":null}}","{\"rateLimits\":{\"primary\":{\"usedPercent\":1e999}}}"}) Check(Parse(invalid).Error!=null,"malformed or empty response");
   using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); bool threw=false; try { UsageReader.Read(null,cancelled.Token); } catch(OperationCanceledException) { threw=true; } Check(threw,"pre-cancel never launches CLI"); }
   Console.WriteLine("Usage tests passed ("+checks+")."); return 0;
  } catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
 }
}
