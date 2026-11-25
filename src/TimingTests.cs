using System;
using System.IO;
using System.Reflection;
using ProgressPeek;
public static class TimingTests {
 [STAThread] public static void Main(){int count=0,failures=0;Run((ok,name)=>{count++;if(!ok){failures++;Console.WriteLine("FAIL: "+name);}});Console.WriteLine("Timing checks: "+count+", failures: "+failures);Environment.ExitCode=failures==0?0:1;}
 public static void Run(Action<bool,string> check){
  var settings=new Settings();check(!settings.ShowEta&&settings.ShowFreshness&&!settings.Paused&&!settings.ManualOnly,"new install requires opting in to beta ETA");
  var t=new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc);var eta=new EtaTracker();eta.Tick(t,true);eta.Add(10,20,t);
  check(eta.Label(t,true,false,false,false)=="Estimating time…","one sample cannot predict duration");
  eta.Tick(t.AddSeconds(60),true);eta.Add(25,35,t.AddSeconds(60));eta.Tick(t.AddSeconds(120),true);eta.Add(40,50,t.AddSeconds(120));
  check(eta.Label(t.AddSeconds(120),true,false,false,false).Contains("min left"),"three advancing timed samples produce rough range");
  check(eta.Label(t.AddSeconds(120),true,false,true,false)=="Paused","pause does not keep counting down");
  check(eta.Label(t.AddSeconds(120),true,false,false,true)=="Time uncertain","unreadable evidence hides time prediction");
  check(eta.Label(t.AddSeconds(500),true,false,false,false)=="Time uncertain","stale progress cannot imply arrival at zero");
  check(eta.Label(t.AddSeconds(120),false,true,false,false)=="Response finished","finished response ends estimate");
  eta.Add(5,15,t.AddSeconds(121));check(eta.Label(t.AddSeconds(121),true,false,false,false)=="Estimating time…","backward reassessment resets velocity history");
  eta.Reset();eta.Tick(t,true);eta.Add(10,20,t);eta.Tick(t.AddSeconds(60),false);eta.Tick(t.AddMinutes(10),false);eta.Tick(t.AddMinutes(11),true);eta.Add(25,35,t.AddMinutes(11));
  check(eta.Label(t.AddMinutes(11),true,false,false,false)=="Estimating time…","paused interval alone does not manufacture progress samples");
  var plateau=new EtaTracker();plateau.Tick(t,true);plateau.Add(20,30,t);plateau.Tick(t.AddSeconds(30),true);plateau.Add(20,30,t.AddSeconds(30));plateau.Tick(t.AddSeconds(60),true);plateau.Add(35,45,t.AddSeconds(60));
  check(plateau.Label(t.AddSeconds(60),true,false,false,false).Contains("min left"),"one plateau participates in total elapsed trend instead of rejecting ETA");
  var slow=new EtaTracker();slow.Tick(t,true);slow.Add(20,30,t);slow.Tick(t.AddSeconds(60),true);slow.Add(21,31,t.AddSeconds(60));slow.Tick(t.AddSeconds(120),true);slow.Add(22,32,t.AddSeconds(120));
  check(slow.Label(t.AddSeconds(120),true,false,false,false).Contains("min left"),"slow genuine progress has a bounded range");
  var clock=new EtaTracker();clock.Tick(t,true);clock.Add(5,15,t);clock.Tick(t.AddSeconds(60),true);clock.Add(15,25,t.AddSeconds(60));clock.Tick(t.AddSeconds(120),true);clock.Add(25,35,t.AddSeconds(120));
  string before=clock.Label(t.AddSeconds(120),true,false,false,false);clock.Tick(t.AddSeconds(240),true);check(clock.Label(t.AddSeconds(240),true,false,false,false)!=before,"valid ETA revises with active elapsed time");
  check(clock.Label(t.AddSeconds(301),true,false,false,false)=="Time uncertain","stale ETA stops before fabricated arrival");
  var flat=new EtaTracker();flat.Tick(t,true);flat.Add(20,30,t);flat.Tick(t.AddSeconds(30),true);flat.Add(20,30,t.AddSeconds(30));flat.Tick(t.AddSeconds(60),true);flat.Add(20,30,t.AddSeconds(60));check(flat.Label(t.AddSeconds(60),true,false,false,false)=="Time uncertain","all plateau evidence cannot invent velocity");
  var huge=new EtaTracker();huge.Tick(t,true);huge.Add(0,0,t);huge.Tick(t.AddSeconds(120),true);huge.Add(1,1,t.AddSeconds(120));huge.Tick(t.AddSeconds(240),true);huge.Add(2,2,t.AddSeconds(240));check(huge.Label(t.AddSeconds(240),true,false,false,false)=="Time uncertain","implausibly distant extrapolation is withheld");
  eta.Reset();eta.Tick(t,true);eta.Add(10,20,t);eta.Tick(t.AddSeconds(60),true);eta.Add(25,35,t.AddSeconds(60));eta.Tick(t.AddSeconds(120),true);eta.Add(40,50,t.AddSeconds(120));eta.Tick(t.AddSeconds(150),true);eta.Add(0,90,t.AddSeconds(150));check(eta.Label(t.AddSeconds(150),true,false,false,false)=="Estimating time…","scope expansion lowering range floor invalidates previous velocity");
  var history=new EtaHistory();var ha=history.Select("A",false,t);ha.Tick(t,true);ha.Tick(t.AddSeconds(60),true);var hb=history.Select("B",false,t.AddSeconds(60));hb.Tick(t.AddSeconds(60),true);hb.Tick(t.AddSeconds(180),true);check(Object.ReferenceEquals(ha,history.Select("A",false,t.AddSeconds(180))),"task cache restores its exact history");ha.Tick(t.AddSeconds(180),true);ha.Tick(t.AddSeconds(240),true);check(Math.Abs(ha.ActiveSeconds-120)<.01,"other task's two minutes never become A active work");ha.Tick(t.AddSeconds(240),false);ha.Tick(t.AddSeconds(270),false);ha.Tick(t.AddSeconds(280),true);ha.Tick(t.AddSeconds(290),true);check(Math.Abs(ha.ActiveSeconds-130)<.01,"pause resume excludes all paused duration");
  for(int i=0;i<33;i++)history.Select("bounded"+i,false,t.AddSeconds(300));check(!Object.ReferenceEquals(ha,history.Select("A",false,t.AddSeconds(300))),"task histories have a bounded least recently used cache");
  check(eta.Explanation.Contains("reassessment"),"reset fallback explains lack of trend evidence");clock.Label(t.AddSeconds(301),true,false,false,false);check(clock.Explanation.Contains("three minutes"),"stale fallback explains expired evidence");
  CheckCardRetention(check,t);
  var gate=new CompletionGate();check(!gate.Update("old",false,true),"opening old finished response does not notify");
  check(!gate.Update("active",true,false)&&gate.Update("active",false,true),"observed running response notifies once on normal end");
  check(!gate.Update("active",false,true),"repeated final scan cannot duplicate notification");
  gate.Update("other",false,true);check(!gate.Update("active",false,true),"switching back to completed response cannot notify again");
  gate.Update("active",true,false);check(gate.Update("active",false,true),"new run with same prompt can notify again");
  gate.Update("cancelled",true,false);check(!gate.Update("cancelled",false,false),"cancelled response is not completion");
 }
 static void CheckCardRetention(Action<bool,string> check,DateTime t){
  string folder=Path.Combine(Path.GetTempPath(),"ProgressPeek-eta-"+Guid.NewGuid().ToString("N"));var flags=BindingFlags.Instance|BindingFlags.NonPublic;Card card=null;
  try{var store=new Storage(folder);store.Settings.ShowEta=true;store.SaveSettings();check(new Storage(folder).Settings.ShowEta,"explicit saved ETA opt-in survives loading");store.Settings.ShowEta=false;store.SaveSettings();check(!new Storage(folder).Settings.ShowEta,"explicit saved ETA opt-out survives loading");
   card=new Card(store,true);var observe=typeof(Card).GetMethod("Observe",flags);var quality=typeof(Card).GetMethod("QualityObservation",flags);var field=typeof(Card).GetField("eta",flags);
   var a=new Snapshot{Chat="ETA A",Prompt="Same prompt",MessageIdentity="a1",ProvenanceKnown=true,AtLatest=true,Output="Working",Generating=true};observe.Invoke(card,new object[]{a});var original=(EtaTracker)field.GetValue(card);original.Reset();original.Tick(t,true);original.Add(10,20,t);original.Tick(t.AddSeconds(60),true);original.Add(25,35,t.AddSeconds(60));original.Tick(t.AddSeconds(120),true);original.Add(40,50,t.AddSeconds(120));
   quality.Invoke(card,new object[]{false,false,t.AddSeconds(120)});observe.Invoke(card,new object[]{new Snapshot{Chat="ETA B",Prompt="Other",TaskIdentity="b1",Output="Working",Generating=true}});quality.Invoke(card,new object[]{false,false,t.AddSeconds(180)});observe.Invoke(card,new object[]{a});quality.Invoke(card,new object[]{false,false,t.AddSeconds(240)});
   var returned=(EtaTracker)field.GetValue(card);check(Object.ReferenceEquals(original,returned)&&returned.Label(t.AddSeconds(240),true,false,false,false).Contains("min left"),"Card A to B to A preserves A timed estimates");
   var etaText=(System.Windows.Controls.TextBlock)typeof(Card).GetField("etaText",flags).GetValue(card);check(etaText.ToolTip!=null&&etaText.ToolTip.ToString().Contains("Experimental"),"ETA tooltip explains experimental evidence");
   a.MessageIdentity="a2";a.PriorResponseFinished=true;observe.Invoke(card,new object[]{a});check(!Object.ReferenceEquals(returned,field.GetValue(card)),"fresh identical submitted prompt receives independent ETA history");
  }finally{if(card!=null)card.Close();if(Directory.Exists(folder))Directory.Delete(folder,true);}
 }
}
