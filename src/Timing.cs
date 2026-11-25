using System;
using System.Collections.Generic;
using System.Linq;
namespace ProgressPeek {
 // A timing heuristic over already-paid-for estimates, not another model request.
 public sealed class EtaTracker {
  sealed class Sample {public double Seconds,Mid;public int Low,High;public DateTime Utc;}
  readonly List<Sample> samples=new List<Sample>();double seconds;DateTime last=DateTime.MinValue;bool active;
  public double ActiveSeconds {get{return seconds;}}
  public string Explanation {get;private set;}
  public void Reset(){samples.Clear();seconds=0;last=DateTime.MinValue;active=false;}
  public void Tick(DateTime now,bool running){if(last!=DateTime.MinValue&&active)seconds+=Math.Max(0,Math.Min(300,(now-last).TotalSeconds));last=now;active=running;}
  public void Add(int low,int high,DateTime now){
   if(low<0||high<low||high>99)return;double mid=(low+high)/2.0;
   if(samples.Count>0&&(mid<samples[samples.Count-1].Mid||low<samples[samples.Count-1].Low||high<samples[samples.Count-1].High))samples.Clear();
   if(samples.Count>0&&seconds-samples[samples.Count-1].Seconds<10)return;
   samples.Add(new Sample{Seconds=seconds,Mid=mid,Low=low,High=high,Utc=now});if(samples.Count>8)samples.RemoveAt(0);
  }
  public string Label(DateTime now,bool running,bool finished,bool paused,bool unknown){
   if(finished)return Result("Response finished","The current response ended locally; project success is not verified.");if(paused)return Result("Paused","Timing is suspended while checks or the response are paused.");if(unknown)return Result("Time uncertain","Current task evidence is unavailable, failed, or interrupted; retained history is not completion.");if(!running)return Result("Awaiting activity","The current response is not actively running.");
   if(samples.Count==0)return Result("Estimating time…","Waiting for already requested progress estimates; no extra AI calls.");var end=samples[samples.Count-1];if((now-end.Utc).TotalSeconds>180)return Result("Time uncertain","The last progress estimate is more than three minutes old. Refreshing evidence may restore a range.");
   if(samples.Count<3)return Result("Estimating time…","At least three estimates separated by active time are needed, including after a backward reassessment.");var start=samples[0];double span=end.Seconds-start.Seconds,gain=end.Mid-start.Mid;
   if(span<30||gain<.5)return Result("Time uncertain","Too little active time or forward progress to extrapolate a duration.");
   // Median of all timed pairs tolerates an isolated plateau and includes its elapsed time.
   var rates=new List<double>();for(int i=0;i<samples.Count;i++)for(int j=i+1;j<samples.Count;j++){double dt=samples[j].Seconds-samples[i].Seconds;if(dt>0)rates.Add((samples[j].Mid-samples[i].Mid)/dt);}
   rates.Sort();double rate=rates.Count==0?0:rates.Count%2==1?rates[rates.Count/2]:(rates[rates.Count/2-1]+rates[rates.Count/2])/2;
   if(rate<=0)return Result("Time uncertain","Recent estimates mostly show a plateau; there is no reliable forward trend.");
   double remaining=(100-end.Mid)/rate;if(Double.IsNaN(remaining)||Double.IsInfinity(remaining)||remaining>10800)return Result("Time uncertain","The observed trend implies more than three hours; this extrapolation is too uncertain.");
   // Revise with observed active time, but hold a defensible floor until new progress arrives.
   double elapsed=Math.Max(0,seconds-end.Seconds);remaining=Math.Max(remaining*.5,remaining-elapsed);
   int lo=Math.Max(1,(int)Math.Floor(remaining*.6/60)),hi=Math.Min(180,Math.Max(lo+1,(int)Math.Ceiling(remaining*1.6/60)));
   return Result("Roughly "+lo+"–"+hi+" min left","A rough trend from recent progress estimates and observed active time, including plateaus. Pauses and other chats are excluded. New evidence can revise the range.");
  }
  string Result(string label,string reason){Explanation=reason;return label;}
 }
 public sealed class EtaHistory {
  readonly Dictionary<string,EtaTracker> tasks=new Dictionary<string,EtaTracker>();readonly List<string> order=new List<string>();string selected="";EtaTracker current;
  public EtaTracker Select(string key,bool fresh,DateTime now){
   if(current!=null&&selected!=key)current.Tick(now,false);
   if(fresh)tasks.Remove(key);EtaTracker found;
   if(String.IsNullOrEmpty(key))found=new EtaTracker();else{if(!tasks.TryGetValue(key,out found)){found=new EtaTracker();tasks[key]=found;}order.Remove(key);order.Add(key);while(order.Count>32){tasks.Remove(order[0]);order.RemoveAt(0);}}
   if(current!=found)found.Tick(now,false);selected=key;current=found;return found;
  }
 }
 public sealed class CompletionGate {
  sealed class State {public bool SawRunning,Fired;}
  readonly Dictionary<string,State> states=new Dictionary<string,State>();readonly Queue<string> order=new Queue<string>();
  public bool Update(string key,bool running,bool finished){
   if(String.IsNullOrEmpty(key))return false;State state;if(!states.TryGetValue(key,out state)){state=new State();states[key]=state;order.Enqueue(key);if(order.Count>32)states.Remove(order.Dequeue());}
   if(running){if(state.Fired){state.Fired=false;state.SawRunning=false;}state.SawRunning=true;return false;}
   if(finished&&state.SawRunning&&!state.Fired){state.Fired=true;return true;}return false;
  }
 }
}
