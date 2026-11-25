using System;
using System.Collections.Generic;
using ProgressPeek;
class Tests {
 static int count;
 static void Assert(bool ok,string name){count++;if(!ok)throw new Exception(name);}
 static void Main(){
  var steps=new List<Step>{new Step{Id="a",Label="Small",Weight=10,State="done",Evidence="Small done"},new Step{Id="b",Label="Large",Weight=90,State="pending",Evidence=""}};
  var p=Estimate.Calculate(steps,"Small done");Assert(p.Known&&p.Low==10&&p.High==10,"unequal effort weights");
  steps[0].Evidence="invented";Assert(!Estimate.Calculate(steps,"reasoning").Known,"unobserved reasoning is unknown");
  steps[0].Evidence="Small done";steps[1].State="active";steps[1].Evidence="Large underway";
  p=Estimate.Calculate(steps,"Small done Large underway");Assert(p.Low==10&&p.High==73,"active effort has a range");
  Assert(!Estimate.Calculate(new List<Step>(),"text").Known,"empty map unknown");
  steps[1].State="done";p=Estimate.Calculate(steps,"Small done Large underway");Assert(p.High==99,"semantic estimate never certifies completion");
  Assert(Evidence.Bound(new string('a',20000),6000).Length==6000,"bounded evidence");
  Assert(Evidence.Hash("a")!=Evidence.Hash("b"),"task identity changes");
  AssertThrows(()=>MapCodec.Parse("garbage",null,""),"malformed JSON");
  AssertThrows(()=>MapCodec.Parse("{\"phase\":\"x\",\"confidence\":\"low\",\"steps\":[]}",null,""),"empty schema");
  string json="{\"phase\":\"Writing\",\"confidence\":\"low\",\"steps\":[{\"id\":\"a\",\"label\":\"Small\",\"weight\":10,\"state\":\"done\",\"evidence\":\"invented\"}]}";
  var map=MapCodec.Parse(json,null,"nothing");Assert(map.Steps[0].State=="pending","unsupported claims removed");
  json=json.Replace("invented","Small done");map=MapCodec.Parse(json,null,"Small done");Assert(map.Steps[0].State=="done","observed evidence retained");
  AssertThrows(()=>MapCodec.Parse(json.Replace("weight\":10","weight\":11"),map,"Small done"),"weights immutable on updates");
  string inferredJson="{\"phase\":\"Implementing\",\"confidence\":\"high\",\"low\":25,\"high\":60,\"inferred\":true,\"explanation\":\"Implementation underway; tests remain\",\"steps\":[{\"id\":\"a\",\"label\":\"Code and verify\",\"weight\":100,\"state\":\"active\",\"evidence\":\"Reasonable inference from task updates\"}]}";
  var inferred=MapCodec.Parse(inferredJson.Replace("\"confidence\":\"high\"","\"confidence\":\"medium\""),null,"Starting implementation");Assert(inferred.Confidence=="medium","inference does not force low confidence");var semantic=Estimate.FromMap(inferred,"Starting implementation");Assert(semantic.Known&&semantic.Low==25&&semantic.High==60,"semantic range is shown without exact completed-work quotation");
  AssertThrows(()=>MapCodec.Parse(inferredJson.Replace("\"low\":25","\"low\":85"),null,""),"reversed semantic range rejected");
  var gate=new RequestGate();Assert(gate.TryReserve("one",DateTime.UtcNow),"first call");Assert(!gate.TryReserve("one",DateTime.UtcNow),"unchanged input blocked");Assert(!gate.TryReserve("two",DateTime.UtcNow),"cooldown enforced");
  for(int i=1;i<3;i++)Assert(gate.TryReserve(i.ToString(),DateTime.UtcNow.AddSeconds(i*91)),"two updates allowed");Assert(!gate.TryReserve("four",DateTime.UtcNow.AddSeconds(300)),"three call ceiling");
  var epoch=new Epoch();int old=epoch.Current;epoch.Advance();Assert(!epoch.IsCurrent(old),"stale results rejected");
  var messageNodes=new[]{new MessageNode("Text","You said:"),new MessageNode("Text","Build the tool"),new MessageNode("Text","2:03 AM"),new MessageNode("Group",""),new MessageNode("Text","Working for 8m 30s"),new MessageNode("Group","")};
  Assert(MessageBoundary.FindPromptEnd(messageNodes,0)==1,"running status is outside prompt even without assistant role");
  messageNodes[4]=new MessageNode("Text","Working for 8m 31s");Assert(MessageBoundary.FindPromptEnd(messageNodes,0)==1,"elapsed time cannot change task identity");
  Assert(MessageBoundary.FindPromptEnd(new[]{new MessageNode("Text","You said:"),new MessageNode("Button","collapsed"),new MessageNode("Text","Full prompt"),new MessageNode("Text","ChatGPT said:")},0)==2,"collapsed user button uses full prompt text");
  Assert(MessageBoundary.SelectComposer(new[]{new ComposerInfo(true,false),new ComposerInfo(false,true)})==1,"hidden old composer cannot pin chat selection");
  Assert(MessageBoundary.IsRunning(new[]{new MessageNode("Text","Working for 2m 10s"),new MessageNode("Edit","draft"),new MessageNode("Button","Send")}),"typing draft cannot hide active generation");
  Assert(!MessageBoundary.IsRunning(new[]{new MessageNode("Text","Worked for 2m 10s"),new MessageNode("Button","Stop dictation")}),"finished task and dictation are not generation");
  Assert(MessageBoundary.FindTaskRole(new[]{new MessageNode("Text","You said:"),new MessageNode("Text","Active task"),new MessageNode("Text","Working for 1m"),new MessageNode("Text","You said:"),new MessageNode("Text","Queued task")})==0,"queued prompt cannot replace currently running task");
  var context=ChatContext.Build("Original build request",new string('r',10000));Assert(context.Contains("Original build request")&&context.Length<=6600,"bounded context keeps original request plus recent chat");
  var status=new CheckState();status.Begin();status.Fail("Sign-in required");Assert(status.Notice=="Sign-in required"&&status.Notice=="Sign-in required","failure persists across refreshes");status.Begin();Assert(status.Notice.Length>0&&status.Busy,"in-flight check stays visible");status.Complete();status.Limit="Task check limit reached";Assert(status.Notice=="Task check limit reached","limit reason stays visible");
  status.Begin();status.Disable();Assert(!status.Busy&&status.Notice.Contains("disabled"),"disabling cancels visible in-flight state");Assert(!CliEstimator.UserError(new FormatException("private transcript")).Contains("private transcript"),"diagnostics never persist model text in format errors");
  var sessions=new SessionCache();sessions.Remember("chat-a",new SessionState{Map=map,Checks=2});sessions.Remember("chat-b",new SessionState{Map=null,Checks=0});Assert(sessions.Find("chat-a")!=null&&sessions.Find("chat-a").Map==map&&sessions.Find("chat-a").Checks==2,"returning to chat restores known progress without new call");Assert(sessions.Find("unseen-chat")==null,"new chat never inherits another task map");
  var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ProgressPeek-test-"+Guid.NewGuid().ToString("N"));var store=new Storage(dir);int used;string reason;
  Assert(store.Reserve("task","one",out used,out reason)&&used==1,"reserve persists before work");store=new Storage(dir);Assert(!store.Reserve("task","one",out used,out reason)&&used==1,"reservation survives restart");
  store.Settings.EnforceDailyLimit=true;store.Settings.DailyChecks=1;Assert(!store.Reserve("second","two",out used,out reason),"daily limit shared across tasks");
  // An ignored toggle, lost persisted setting, or reset counter must fail these checks.
  var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
  store.Settings=serializer.Deserialize<Settings>("{\"EnforceDailyLimit\":false,\"DailyChecks\":1}");
  Assert(store.Reserve("second","two",out used,out reason)&&used==1,"disabled daily cap permits checks above cap");
  Assert(store.Ledger.DailyCalls==2,"disabled daily cap still counts usage");
  store.SaveSettings();store=new Storage(dir);
  Assert(store.Reserve("third","three",out used,out reason),"daily cap toggle survives restart");
  Assert(!store.Reserve("third","three",out used,out reason)&&reason=="Evidence unchanged","disabled daily cap retains duplicate protection");
  Assert(!store.Reserve("third","changed",out used,out reason)&&reason=="Waiting for new evidence","disabled daily cap retains cooldown");
  store.Ledger.Tasks["third"].Calls=3;
  Assert(!store.Reserve("third","changed",out used,out reason)&&reason=="Task check limit reached","disabled daily cap retains task ceiling");
  store.Settings=serializer.Deserialize<Settings>("{\"EnforceDailyLimit\":true,\"DailyChecks\":1}");
  Assert(!store.Reserve("fourth","four",out used,out reason)&&reason=="Daily check limit reached","re-enabled daily cap uses accumulated usage");
  Assert(store.Ledger.DailyCalls==3,"re-enabling daily cap never clears usage");
  store.Settings=serializer.Deserialize<Settings>("{\"DailyChecks\":1}");
  Assert(store.Reserve("legacy","legacy",out used,out reason),"settings without an explicit daily preference use the new off default");
  var events=new CliEvents();events.Accept("not json");events.Accept("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"{}\"}}");Assert(events.Final=="{}"&&!events.Completed,"agent message alone not completion");events.Accept("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":22,\"output_tokens\":4}}");Assert(events.Completed&&events.InputTokens==22&&events.OutputTokens==4,"CLI usage parsed");events.Accept("{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\"}}");Assert(events.ToolUsed,"unexpected tool rejected");Assert(CliEstimator.IsPlanLogin("Logged in using ChatGPT")&&!CliEstimator.IsPlanLogin("Logged in using an API key"),"API authentication refused");
  var failedEvents=new CliEvents();failedEvents.Accept("{\"type\":\"turn.failed\",\"error\":{\"message\":\"invalid_json_schema: synthetic private text\"}}");
  Assert(failedEvents.SafeFailure(1,"").Contains("schema"),"failed schema event has actionable classification");
  Assert(!failedEvents.SafeFailure(1,"").Contains("synthetic private text"),"safe failure classification never exposes input");
  Assert(new CliEvents().SafeFailure(1,"error: unexpected argument '--badflag' found").Contains("configuration"),"CLI argument failure classified separately");
  Assert(new CliEvents().SafeFailure(1,"rate_limit_exceeded: private text").Contains("allowance"),"allowance failure classified separately");
  Assert(new CliEvents().SafeFailure(1,"network connection failed").Contains("connection"),"connection failure classified separately");
  Assert(new CliEvents().SafeFailure(1,"unknown private text").Contains("exit code 1"),"unknown exit retains exit code without raw output");
  Assert(new CliEvents{Completed=true}.SafeFailure(0,"").Contains("no final"),"missing final response classified separately");
  Assert(new CliEvents{Completed=true,Final="{}",ToolUsed=true}.SafeFailure(0,"").Contains("tools"),"actual tool rejection classified separately");
  Assert(new CliEvents{Completed=true,Final="{}"}.SafeFailure(0,"")==null,"successful response not classified as failure");
  Assert(new CliEvents().SafeFailure(1,"Error: stream did not contain valid UTF-8").Contains("encoding"),"invalid UTF-8 failure identifies encoding rather than tools");
  using(var unicodeInput=new System.IO.MemoryStream()){CliEstimator.WriteInput(unicodeInput,"café — progressing…");Assert(new System.Text.UTF8Encoding(false,true).GetString(unicodeInput.ToArray())=="café — progressing…","CLI stdin preserves Unicode as UTF-8 independent of Windows code page");}
  string scoped="{\"phase\":\"Answering\",\"confidence\":\"medium\",\"low\":20,\"high\":30,\"inferred\":true,\"explanation\":\"Answer underway\",\"scope_size\":\"short\",\"steps\":[{\"id\":\"answer\",\"label\":\"Answer question\",\"weight\":100,\"state\":\"active\",\"evidence\":\"Writing answer\"}]}";
  bool acceptedScope=false;try{acceptedScope=MapCodec.Parse(scoped,null,"").Low==20;}catch(FormatException){}Assert(acceptedScope,"model scope classification accepted alongside progress");
  var adaptiveSettings=new Settings{AutoIntervalSeconds=30};var shortMap=MapCodec.Parse(scoped,null,"");
  Assert(CheckCadence.Interval(adaptiveSettings,shortMap)==15,"short task refreshes sooner after model scope assessment");
  adaptiveSettings.AdaptiveTiming=false;Assert(CheckCadence.Interval(adaptiveSettings,shortMap)==30,"adaptive off respects selected fixed interval");adaptiveSettings.AdaptiveTiming=true;shortMap.ScopeSize="long";Assert(CheckCadence.Interval(adaptiveSettings,shortMap)==30&&CheckCadence.Interval(adaptiveSettings,null)==30,"large and unknown workloads keep configured interval");
  var cadenceStore=new Storage(System.IO.Path.Combine(dir,"cadence"));cadenceStore.Settings.EnforceDailyLimit=false;var tick=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc);
  Assert(cadenceStore.Reserve("adaptive","a",false,tick,15,out used,out reason)&&!cadenceStore.Reserve("adaptive","b",false,tick.AddSeconds(14),15,out used,out reason)&&cadenceStore.Reserve("adaptive","b",false,tick.AddSeconds(15),15,out used,out reason),"adaptive interval enforced at reservation boundary");
  cadenceStore.Settings.EnforceDailyLimit=true;cadenceStore.Settings.DailyChecks=2;Assert(!cadenceStore.Reserve("adaptive","c",false,tick.AddSeconds(30),15,out used,out reason),"adaptive cadence never bypasses daily allowance cap");
  Assert(!cadenceStore.Reserve("fixed","a",false,tick,30,out used,out reason),"adaptive shares account usage ledger with fixed checks");
  adaptiveSettings.AdaptiveTiming=false;cadenceStore.Settings=adaptiveSettings;cadenceStore.SaveSettings();Assert(!new Storage(cadenceStore.Folder).Settings.AdaptiveTiming,"adaptive off persists across restarts");
  AssertThrows(()=>MapCodec.Parse(scoped.Replace("\"short\"","\"nonsense\""),null,""),"unknown scope classification rejected");
  System.IO.Directory.Delete(dir,true);Console.WriteLine("PASS "+count+" assertions");
 }
 static void AssertThrows(Action a,string name){bool thrown=false;try{a();}catch{thrown=true;}Assert(thrown,name);}
}
