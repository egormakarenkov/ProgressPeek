using System;
using System.Collections.Generic;
namespace ProgressPeek {
 // In-memory only. A steering message belongs to the active response, not a new task.
 public class TaskScope {
  class State {public string Original,Latest,MessageIdentity="",TaskIdentity="",Additions="";public bool Closed;public DateTime Started;}
  readonly Dictionary<string,State> states=new Dictionary<string,State>();readonly List<string> order=new List<string>();
  public Snapshot Normalize(Snapshot source,DateTime now){
   if(source==null||source.ScopeNormalized||!String.IsNullOrEmpty(source.Error)||String.IsNullOrWhiteSpace(source.Prompt))return source;
   if(source.ProvenanceKnown&&(!source.AtLatest||String.IsNullOrEmpty(source.MessageIdentity))){var gap=source.Clone();gap.Error="Latest response is not accessible at the current scroll position";gap.Generating=false;gap.FinalVisible=false;gap.Terminal="";gap.ResponseReadComplete=false;return gap;}
   string key=Lifecycle.ChatKey(source);State state;bool exists=states.TryGetValue(key,out state);
   bool newMessage=exists&&(source.ProvenanceKnown?source.MessageIdentity!=state.MessageIdentity:source.Prompt!=state.Latest);
   bool restart=state!=null&&state.Closed&&source.Generating&&(!source.ProvenanceKnown||newMessage);
   if(!exists||restart||newMessage&&(state.Closed||source.PriorResponseFinished)){
    state=new State{Original=source.Prompt,Latest=source.Prompt,MessageIdentity=source.MessageIdentity,TaskIdentity=source.ProvenanceKnown?source.MessageIdentity:"",Started=now};states[key]=state;
   }else if(newMessage){
    state.Latest=source.Prompt;state.MessageIdentity=source.MessageIdentity;state.Additions=Evidence.Bound(state.Additions+"\n\nFollow-up:\n"+source.Prompt,4000);
   }
   order.Remove(key);order.Add(key);if(order.Count>8){states.Remove(order[0]);order.RemoveAt(0);}
   var normalized=source.Clone();normalized.ScopeNormalized=true;normalized.Prompt=state.Original;normalized.FollowUps=state.Additions;normalized.TaskIdentity=state.TaskIdentity;if(source.ProvenanceKnown&&state.Closed&&source.Generating){normalized.Error="Waiting for the current response state";normalized.Generating=false;normalized.FinalVisible=false;}return normalized;
  }
  public void End(Snapshot s){State state;if(s!=null&&states.TryGetValue(Lifecycle.ChatKey(s),out state))state.Closed=true;}
  public bool Ready(Snapshot s,DateTime now){State state;return s!=null&&states.TryGetValue(Lifecycle.ChatKey(s),out state)&&(now-state.Started).TotalSeconds>=10;}
 }
}
