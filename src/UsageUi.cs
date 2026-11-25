using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
namespace ProgressPeek {
 public static class UsageFooter {
  public static string Format(UsageSnapshot snapshot,DateTime now,bool failed){
   if(snapshot==null||snapshot.Windows.Count==0)return "Account usage unavailable";
   bool multi=snapshot.Windows.Select(w=>w.Bucket).Distinct().Count()>1;
   long unix=(long)(now.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
   return String.Join("\n",snapshot.Windows.Select(w=>{
    bool stale=failed||(now.ToUniversalTime()-snapshot.FetchedUtc).TotalMinutes>10||w.ResetsAt.HasValue&&w.ResetsAt.Value<=unix;
    return (multi?w.Bucket+" · ":"")+w.Label+" · "+(Double.IsNaN(w.Remaining)?"unavailable":Math.Floor(w.Remaining).ToString("0")+"% left")+(stale?" · stale":"");
   }));
  }
 }
 public partial class Card {
  readonly System.Collections.Generic.Dictionary<string,long> chatTokenTotals=new System.Collections.Generic.Dictionary<string,long>();
  void RecordTokenUsage(Snapshot scan,CliResult result,TokenTally tally){if(scan==null||result==null)return;long added=Math.Max(0,result.InputTokens)+Math.Max(0,result.OutputTokens);tally.Total+=added;string key=Evidence.Hash(Lifecycle.ChatKey(scan));long previous;chatTokenTotals.TryGetValue(key,out previous);chatTokenTotals[key]=previous+added;}
  DispatcherTimer usageTimer;CancellationTokenSource usageCancel;UsageSnapshot usageSnapshot;DateTime usageAttempt=DateTime.MinValue;int usageEpoch;bool usageBusy,usageFailed;
  void StartUsage(){
   RenderUsage();
   if(demo)return;if(!store.Settings.ShowUsage){StopUsage();return;}
   if(usageTimer==null){usageCancel=new CancellationTokenSource();usageTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};usageTimer.Tick+=(s,e)=>{RenderUsage();if((DateTime.UtcNow-usageAttempt).TotalSeconds>=30)RequestUsage();};usageTimer.Start();RequestUsage();}
  }
  void StopUsage(){usageEpoch++;if(usageCancel!=null){usageCancel.Cancel();usageCancel.Dispose();usageCancel=null;}if(usageTimer!=null){usageTimer.Stop();usageTimer=null;}usageBusy=false;usageSnapshot=null;usageFailed=false;}
  void RequestUsage(){
   if(closed||demo||usageBusy||!store.Settings.ShowUsage||usageCancel==null)return;usageBusy=true;usageAttempt=DateTime.UtcNow;int captured=usageEpoch;var token=usageCancel.Token;if(usageSnapshot==null)RenderUsage();
   Task.Run(()=>{UsageSnapshot result;try{result=UsageReader.Read(store,token);}catch(OperationCanceledException){return;}catch{result=new UsageSnapshot{Error="Account usage unavailable"};}
    if(closed||Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(()=>{if(closed||captured!=usageEpoch||!store.Settings.ShowUsage)return;usageBusy=false;usageFailed=!String.IsNullOrEmpty(result.Error);if(!usageFailed||usageSnapshot==null)usageSnapshot=result;RenderUsage();}));
   });
  }
  void RenderUsage(){
   bool tokens=store.Settings.TokenDisplay!="Hidden";string account=store.Settings.ShowUsage?(demo?"Weekly · 93% left (demo)":usageSnapshot==null&&usageBusy?"Reading account usage…":UsageFooter.Format(usageSnapshot,DateTime.UtcNow,usageFailed)):"";
   string counter="";if(tokens){bool chat=store.Settings.TokenDisplay=="Chat";long total=taskTokens.Total;if(chat){total=0;if(current!=null)chatTokenTotals.TryGetValue(Evidence.Hash(Lifecycle.ChatKey(current)),out total);}counter=(chat?"Widget chat":"Widget task")+" · "+(current==null?"—":"~"+total.ToString("N0"))+" tokens";}
   usageText.Text=account+(account.Length>0&&counter.Length>0?"  |  ":"")+counter;usageText.Visibility=!collapsed&&(store.Settings.ShowUsage||tokens)?Visibility.Visible:Visibility.Collapsed;
   usageText.ToolTip="Account allowance refreshes every 30 seconds without an AI call; service reporting can lag.\nWidget tokens: reported input + output for estimation requests, not the main ChatGPT conversation. Totals reset on app restart; task totals also expire with the task cache. Cancelled or failed checks without reported usage are not included."+(usageSnapshot!=null?"\nUsage last checked: "+usageSnapshot.FetchedUtc.ToLocalTime().ToString("g"):"");
  }
 }
}
