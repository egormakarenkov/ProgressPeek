using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace ProgressPeek {
 public static class CheckCadence {public static int Interval(Settings settings,TaskMap map){int configured=Math.Max(15,Math.Min(300,settings.AutoIntervalSeconds));return settings.AdaptiveTiming&&map!=null&&map.ScopeSize=="short"?15:configured;}}
 public class Step {public string Id,Label,State,Evidence;public double Weight;}
 public class TaskMap {public string Phase,Confidence,Explanation="",ScopeSize="unknown";public int Low=-1,High=-1;public bool Inferred;public List<Step> Steps;}
 public class Estimate {
  public bool Known;public int Low,High;public string EvidenceText;
  public static Estimate FromMap(TaskMap map,string observed){if(map!=null&&map.Low>=0&&map.High>=map.Low&&map.High<=99)return new Estimate{Known=true,Low=map.Low,High=map.High,EvidenceText=map.Explanation};return Calculate(map==null?null:map.Steps,observed);}
  public static Estimate Calculate(List<Step> steps,string observed){
   var result=new Estimate();if(steps==null||steps.Count==0)return result;
   double total=steps.Sum(s=>s.Weight),done=0,active=0;if(total<=0)return result;
   foreach(var s in steps){if(String.IsNullOrWhiteSpace(s.Evidence)||observed.IndexOf(s.Evidence,StringComparison.Ordinal)<0)continue;if(s.State=="done")done+=s.Weight;else if(s.State=="active")active+=s.Weight;else continue;result.EvidenceText=s.Evidence;}
   result.Known=done>0||active>0;if(!result.Known)return result;
   result.Low=Math.Min(99,(int)Math.Floor(100*done/total));result.High=Math.Min(99,(int)Math.Ceiling(100*(done+active*.7)/total));return result;
  }
 }
 public static class Evidence {
  public static string Bound(string text,int max){text=text??"";return text.Length<=max?text:text.Substring(text.Length-max);}
  public static string BoundRequest(string text,int max){text=text??"";if(text.Length<=max)return text;const string gap="\n[Middle omitted]\n";int keep=max-gap.Length;return text.Substring(0,keep/2)+gap+text.Substring(text.Length-(keep-keep/2));}
  public static string Hash(string text){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text??"")));}
 }
 public static class MapCodec {
  static int Integer(object value){if(!(value is int)&&!(value is long))throw new FormatException("Invalid estimate range");return checked(Convert.ToInt32(value));}
  static string StringField(Dictionary<string,object> data,string key){object value;if(!data.TryGetValue(key,out value)||!(value is string))throw new FormatException("Invalid step values");return (string)value;}
  static double WeightField(Dictionary<string,object> data){object value;if(!data.TryGetValue("weight",out value)||!(value is int||value is long||value is double||value is decimal))throw new FormatException("Invalid step values");return Convert.ToDouble(value);}
  public static TaskMap Parse(string json,TaskMap previous,string observed){
   var serializer=new JavaScriptSerializer{MaxJsonLength=100000};var root=serializer.DeserializeObject(json) as Dictionary<string,object>;
   if(root==null||(root.Count!=3&&root.Count!=7&&root.Count!=8)||!root.ContainsKey("phase")||!root.ContainsKey("confidence")||!root.ContainsKey("steps"))throw new FormatException("Invalid task map");
   string scope="unknown";if(root.Count==8){scope=StringField(root,"scope_size");if(!(new[]{"short","medium","long","unknown"}).Contains(scope))throw new FormatException("Invalid map fields");}
   bool semantic=root.Count>=7;int low=-1,high=-1;bool inferred=false;string explanation="";if(semantic){low=Integer(root["low"]);high=Integer(root["high"]);if(!(root["inferred"] is bool))throw new FormatException("Invalid map fields");inferred=(bool)root["inferred"];explanation=root["explanation"] as string;if(low<0||high<low||high>99||explanation==null||explanation.Length>400)throw new FormatException("Invalid estimate range");}
   var phase=root["phase"] as string;var confidence=root["confidence"] as string;var array=root["steps"] as object[];
   if(phase==null||phase.Length>80||!(new[]{"low","medium","high"}).Contains(confidence)||array==null||array.Length<1||array.Length>12)throw new FormatException("Invalid map fields");
   var steps=new List<Step>();var ids=new HashSet<string>();
   foreach(object o in array){var d=o as Dictionary<string,object>;if(d==null||d.Count!=5)throw new FormatException("Invalid step");
    var s=new Step{Id=StringField(d,"id"),Label=StringField(d,"label"),Weight=WeightField(d),State=StringField(d,"state"),Evidence=StringField(d,"evidence")};
    if(s.Id.Length<1||s.Id.Length>40||!ids.Add(s.Id)||s.Label.Length<1||s.Label.Length>120||Double.IsNaN(s.Weight)||s.Weight<1||s.Weight>100||!(new[]{"pending","active","done"}).Contains(s.State)||s.Evidence.Length>300)throw new FormatException("Invalid step values");
    if(!semantic&&s.State!="pending"&&(String.IsNullOrWhiteSpace(s.Evidence)||observed.IndexOf(s.Evidence,StringComparison.Ordinal)<0)){s.State="pending";s.Evidence="";}
    if(previous!=null){var prior=previous.Steps.FirstOrDefault(x=>x.Id==s.Id);if(!semantic&&(prior==null||prior.Label!=s.Label||prior.Weight!=s.Weight))throw new FormatException("Task map changed");if(semantic&&prior!=null){s.Label=prior.Label;s.Weight=prior.Weight;}}
    steps.Add(s);
   }
   if(previous!=null&&!semantic&&steps.Count!=previous.Steps.Count)throw new FormatException("Task map changed");return new TaskMap{Phase=phase,Confidence=confidence,Steps=steps,Low=low,High=high,Inferred=inferred,Explanation=explanation,ScopeSize=scope};
  }
 }
 public class MessageNode {public string Kind,Name;public MessageNode(string kind,string name){Kind=kind;Name=name;}}
 public static class ChatContext {
  public static string Build(string original,string recent){original=original??"";return "Original request:\n"+original.Substring(0,Math.Min(original.Length,2500))+"\nRecent chat:\n"+Evidence.Bound(recent,4000);}
 }
 public class ComposerInfo {public bool Offscreen,HasBounds;public ComposerInfo(bool offscreen,bool hasBounds){Offscreen=offscreen;HasBounds=hasBounds;}}
 public static class MessageBoundary {
  public static bool IsFinished(IList<MessageNode> nodes){return !IsRunning(nodes)&&nodes.Any(n=>((n.Kind=="Text"||n.Kind=="Button")&&n.Name.StartsWith("Worked for ",StringComparison.Ordinal))||(n.Kind=="Button"&&(n.Name=="Copy response"||n.Name=="Good response"||n.Name=="Bad response"||n.Name=="Read aloud")));}
  public static T ComposerBranch<T>(T composer,T scope,Func<T,T> parent,Func<T,T,bool> equal) where T:class {var branch=composer;for(int i=0;i<12&&branch!=null;i++){var next=parent(branch);if(next==null||equal(next,scope))return branch;branch=next;}return null;}
  public static bool IsStopControl(MessageNode node){return node.Kind=="Button"&&(node.Name=="Stop"||node.Name=="Stop generating");}
  public static bool IsRunning(IList<MessageNode> nodes){return nodes.Any(n=>(n.Kind=="Button"&&(n.Name=="Stop"||n.Name=="Stop generating"))||(n.Kind=="Text"&&(n.Name=="Thinking"||n.Name=="Thinking…"||n.Name=="Thinking..."||n.Name.StartsWith("Working for ",StringComparison.Ordinal))));}
  public static int FindTaskRole(IList<MessageNode> nodes){int role=-1,runningRole=-1;for(int i=0;i<nodes.Count;i++){if(nodes[i].Name=="You said:")role=i;if(nodes[i].Name.StartsWith("Working for ",StringComparison.Ordinal))runningRole=role;}return runningRole>=0?runningRole:role;}
  public static int SelectComposer(IList<ComposerInfo> composers){for(int i=0;i<composers.Count;i++)if(!composers[i].Offscreen&&composers[i].HasBounds)return i;return -1;}
  public static bool IsChrome(string name){return name=="ChatGPT said:"||name=="ChatGPT"||name=="Latest response"||System.Text.RegularExpressions.Regex.IsMatch(name??"",@"^(\d{1,2}:\d{2}\s*(AM|PM)|Working for\s|Worked for\s)");}
  public static int FindPromptEnd(IList<MessageNode> nodes,int role){int last=-1;for(int i=role+1;i<nodes.Count;i++){var n=nodes[i];if(IsChrome(n.Name)||n.Name=="You said:"||n.Kind=="Edit")break;if(n.Kind=="Text"&&!String.IsNullOrWhiteSpace(n.Name))last=i;}return last;}
 }
 public class TokenTally {public long Total;} public class SessionState {public TokenTally Tokens=new TokenTally();public TaskMap Map;public int Checks;public long InputTokens,OutputTokens;public DateTime LastEstimateUtc;}
 public class CheckState {
  public bool Busy;public string Error="",Limit="";
  public void Begin(){Busy=true;Error="";Limit="";}
  public void Fail(string error){Busy=false;Error=error;}
  public void Complete(){Busy=false;Error="";}
  public void Disable(){Busy=false;Error="AI estimates disabled; current check cancelled.";Limit="";}
  public string Notice {get{return Busy?"Luna is estimating this task…":(!String.IsNullOrEmpty(Error)?Error:Limit);}}
 }
 public class SessionCache {
  public void Forget(string key){states.Remove(key);order.Remove(key);}
  readonly Dictionary<string,SessionState> states=new Dictionary<string,SessionState>();readonly List<string> order=new List<string>();
  public void Remember(string key,SessionState state){if(String.IsNullOrEmpty(key)||state==null)return;order.Remove(key);order.Add(key);states[key]=state;if(order.Count>8){states.Remove(order[0]);order.RemoveAt(0);}}
  public SessionState Find(string key){SessionState state;return states.TryGetValue(key,out state)?state:null;}
 }
 public class RequestGate {
  public int Calls;public DateTime Last=DateTime.MinValue;public string LastHash="";
  public bool TryReserve(string hash,DateTime now){if(Calls>=3||hash==LastHash||(now-Last).TotalSeconds<90)return false;Calls++;Last=now;LastHash=hash;return true;}
 }
 public class Epoch {private int current;public int Current{get{return System.Threading.Volatile.Read(ref current);}}public int Advance(){return System.Threading.Interlocked.Increment(ref current);}public bool IsCurrent(int value){return value==Current;}}
}
