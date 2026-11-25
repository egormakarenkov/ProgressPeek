using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using ProgressPeek;
static class ScopeFlowTests {
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(Card c,string name){return typeof(Card).GetField(name,Hidden).GetValue(c);}
 static void Observe(Card c,Snapshot s){typeof(Card).GetMethod("Observe",Hidden).Invoke(c,new object[]{s});}
 public static void Run(Action<bool,string> check,string folder){
  var card=new Card(new Storage(Path.Combine(folder,"scope")),true);
  try{
   var first=new Snapshot{Handle=new IntPtr(42),Chat="Scope",Prompt="Build a complete editor, test it and package it",Generating=true,Output="Implementing editor"};Observe(card,first);
   var epoch=(Epoch)Field(card,"epoch");int ticket=epoch.Current;
   var follow=new Snapshot{Handle=first.Handle,Chat=first.Chat,Prompt="Yes, use blue",Generating=true,Output="Applying the requested accent"};Observe(card,follow);
   check(((Snapshot)Field(card,"current")).Prompt==first.Prompt,"ongoing follow-up retains original task scope");
   check(epoch.IsCurrent(ticket),"ongoing follow-up does not reset task identity");
   check(first.Prompt=="Build a complete editor, test it and package it"&&follow.Prompt=="Yes, use blue","scope normalization does not mutate captured snapshots");
   typeof(Card).GetField("map",Hidden).SetValue(card,new TaskMap{Low=20,High=30,Confidence="low",Inferred=true,Phase="Coding",Explanation="Code in progress"});Observe(card,follow);
   check(((TextBlock)Field(card,"hint")).Text=="Approximate progress","main card uses neutral approximate progress label");
   check(((TextBlock)Field(card,"confidence")).Text.Contains("low confidence"),"details retain actual evidence confidence");
   check(((Snapshot)Field(card,"current")).FollowUps.Contains("Yes, use blue"),"estimator receives follow-up alongside anchored scope");
  }finally{card.Close();}
  var time=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc);var tracker=new TaskScope();
  var start=new Snapshot{Chat="A",Prompt="Build full project",Generating=true};var pinned=tracker.Normalize(start,time);
  check(!tracker.Ready(pinned,time.AddSeconds(9))&&tracker.Ready(pinned,time.AddSeconds(10)),"brief initial grace avoids checking very short responses");
  var addition=new Snapshot{Chat="A",Prompt="Answer to clarification",Generating=true};var joined=tracker.Normalize(addition,time.AddSeconds(12));
  check(joined.Prompt=="Build full project"&&joined.FollowUps.Contains("Answer to clarification")&&tracker.Ready(joined,time.AddSeconds(12)),"follow-up does not restart initial grace");
  var repeated=tracker.Normalize(joined,time.AddSeconds(13));check(repeated.FollowUps==joined.FollowUps,"normalized observations are idempotent");
  var steering=new TaskScope();steering.Normalize(new Snapshot{Chat="Color",Prompt="Build editor",Generating=true},time);steering.Normalize(new Snapshot{Chat="Color",Prompt="Use blue",Generating=true},time);steering.Normalize(new Snapshot{Chat="Color",Prompt="Use red",Generating=true},time);var blue=steering.Normalize(new Snapshot{Chat="Color",Prompt="Use blue",Generating=true},time);check(blue.FollowUps.EndsWith("Use blue"),"later repeated steering text retains chronological priority");
  var originalAgain=steering.Normalize(new Snapshot{Chat="Color",Prompt="Build editor",Generating=true},time);check(originalAgain.FollowUps.EndsWith("Build editor"),"raw repeat of original request is not confused with normalized scan");
  tracker.Normalize(new Snapshot{Chat="B",Prompt="Unrelated question",Generating=true},time);check(tracker.Normalize(addition,time.AddSeconds(15)).Prompt=="Build full project","chat switching preserves independent task scopes");
  var next=new Snapshot{Chat="A",Prompt="A genuinely new task",Generating=true,PriorResponseFinished=true};check(tracker.Normalize(next,time.AddSeconds(16)).Prompt==next.Prompt,"positive prior finish separates next request");
  tracker.End(next);var after=new Snapshot{Chat="A",Prompt="Another independent task",Generating=true};check(tracker.Normalize(after,time.AddSeconds(20)).Prompt==after.Prompt,"confirmed completion releases scope anchor");
  var lastAddition=new Snapshot{Chat="A",Prompt="Use green",Generating=true};tracker.Normalize(lastAddition,time.AddSeconds(21));tracker.End(lastAddition);var replay=tracker.Normalize(lastAddition,time.AddSeconds(22));check(replay.Prompt=="Use green"&&replay.FollowUps=="","repeating a previous follow-up after finish creates fresh scope");
  var cycle=new Lifecycle();var final=new Snapshot{Chat="A",Prompt="Question",Output="Answer",FinalVisible=true,ResponseReadComplete=false};cycle.Update(final,time);cycle.Update(final,time.AddSeconds(10));check(!cycle.Finished,"incomplete status scan cannot certify completion");final.ResponseReadComplete=true;cycle.Update(final,time.AddSeconds(11));cycle.Update(final,time.AddSeconds(12));check(cycle.Finished,"complete response footer ends locally without model estimate");
  var payload=CliEstimator.BuildInput("ORIGINAL "+new string('a',15000)+" END", "Visible output","Background","Use blue",null);var data=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(payload.Substring(payload.IndexOf("\nDATA=")+6)) as System.Collections.Generic.Dictionary<string,object>;
  check(((string)data["original_request"]).StartsWith("ORIGINAL ")&&((string)data["original_request"]).EndsWith(" END")&&((string)data["original_request"]).Length<=12000,"bounded original scope preserves beginning and ending");
  check((string)data["follow_up_additions"]=="Use blue"&&(string)data["observed"]=="Visible output","estimator input separates original scope additions and evidence");
 }
}
