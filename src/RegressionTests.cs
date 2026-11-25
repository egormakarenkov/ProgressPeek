using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ProgressPeek;

class RegressionTests {
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 static int checks,failures;
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Assert(bool ok,string name){checks++;if(!ok){failures++;Console.WriteLine("FAIL: "+name);}}
 static object Field(object target,string name){var f=target.GetType().GetField(name,Hidden);return f==null?null:f.GetValue(target);}
 static void Set(object target,string name,object value){var f=target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public);if(f!=null)f.SetValue(target,value);}
 static void Observe(Card card,Snapshot snapshot){card.GetType().GetMethod("Observe",Hidden).Invoke(card,new object[]{snapshot});}
 static string Text(Card card,string name){return ((TextBlock)Field(card,name)).Text;}
 static Snapshot Scan(bool running){return new Snapshot{Handle=new IntPtr(123),Chat="Fixture chat",ChatIdentity="runtime-1",Prompt="Implement the current change",Output="Coding and tests underway",Generating=running};}
 static TaskMap Map(){return new TaskMap{Phase="Coding",Confidence="low",Low=20,High=30,Inferred=true,Explanation="Implementation is underway",Steps=new List<Step>{new Step{Id="code",Label="Code and verify",Weight=100,State="active",Evidence="Reported coding"}}};}
 static void CheckDragging(Card card,Storage store){
  card.Show();card.UpdateLayout();
  var surface=card.GetType().GetMethod("IsDragSurface",Hidden);
  Assert(surface!=null,"top-band drag hit testing exists");
  if(surface!=null){
   Func<Point,bool> hit=p=>(bool)surface.Invoke(card,new object[]{p});
   Assert(hit(new Point(30,5))&&hit(new Point(card.ActualWidth/2,5))&&hit(new Point(card.ActualWidth-20,5)),"padding above title and controls is draggable across top band");
   Assert(hit(new Point(5,30))&&hit(new Point(40,30))&&hit(new Point(card.ActualWidth-5,30)),"title and both side margins are draggable");
   var button=(Button)Field(card,"refresh");var center=button.TranslatePoint(new Point(button.ActualWidth/2,button.ActualHeight/2),card);
   Assert(!hit(center),"refresh remains clickable rather than starting a drag");
   var header=(Grid)button.Parent;var menu=(Button)header.Children[2];
   Assert(!hit(menu.TranslatePoint(new Point(menu.ActualWidth/2,menu.ActualHeight/2),card)),"menu remains clickable rather than starting a drag");
   Assert(!hit(new Point(50,100)),"body does not become a drag surface");
  }
  var saved=Field(card,"current");double offsetX=store.Settings.OffsetX,offsetY=store.Settings.OffsetY;
  var anchor=new Window{Width=100,Height=100,Left=0,Top=0,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Opacity=0};anchor.Show();
  try{
   card.GetType().GetField("current",Hidden).SetValue(card,new Snapshot{Handle=new System.Windows.Interop.WindowInteropHelper(anchor).Handle});
   store.Settings.OffsetX=0;store.Settings.OffsetY=0;
   var follow=card.GetType().GetMethod("Follow",Hidden);var moving=card.GetType().GetField("movingWindow",Hidden);
   Assert(moving!=null,"native drag suspends automatic repositioning");
   if(moving!=null){moving.SetValue(card,true);bool stable=true;foreach(double x in new[]{150d,150.5,151d,450d,152d}){card.Left=x;card.Top=150;double placedX=card.Left,placedY=card.Top;follow.Invoke(card,null);stable&=card.Left==placedX&&card.Top==placedY;}Assert(stable,"follow ticks cannot fight small or large movements during a drag");moving.SetValue(card,false);}
   var grip=(System.Windows.Controls.Primitives.Thumb)Field(card,"resizeGrip");grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});card.Left=155;card.Top=150;double resizeX=card.Left,resizeY=card.Top;follow.Invoke(card,null);Assert(card.Left==resizeX&&card.Top==resizeY,"follow ticks cannot fight a resize gesture");
   grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});follow.Invoke(card,null);Assert(Math.Abs(card.Left-155)<1&&Math.Abs(card.Top-150)<1,"release persists position before following resumes");
   anchor.Left=20;follow.Invoke(card,null);Assert(Math.Abs(card.Left-175)<1,"following resumes when anchor moves after gesture");
  }finally{anchor.Close();card.GetType().GetField("current",Hidden).SetValue(card,saved);store.Settings.OffsetX=offsetX;store.Settings.OffsetY=offsetY;}
 }
 static void CheckQualityOfLife(string folder){
  var store=new Storage(Path.Combine(folder,"quality"));store.Settings.Enabled=true;var card=new Card(store,true);
  try{
   var s=Scan(true);Observe(card,s);card.GetType().GetField("map",Hidden).SetValue(card,Map());Observe(card,s);card.Show();card.UpdateLayout();
   var collapse=card.GetType().GetMethod("ToggleCollapse",Hidden);collapse.Invoke(card,null);card.UpdateLayout();
   Assert(Text(card,"compactPercent")=="20–30%"&&((FrameworkElement)Field(card,"compactPercent")).Visibility==Visibility.Visible,"collapsed header retains percentage");
   Assert(((FrameworkElement)Field(card,"body")).Visibility==Visibility.Collapsed&&card.ActualHeight<120,"collapsed card stays a small rectangle");
   collapse.Invoke(card,null);
   store.Settings.Paused=true;card.GetType().GetField("busy",Hidden).SetValue(card,true);var epoch=(Epoch)Field(card,"epoch");int ticket=epoch.Current;card.GetType().GetMethod("ApplyPreferences",Hidden).Invoke(card,null);
   Assert(!(bool)Field(card,"busy")&&!epoch.IsCurrent(ticket)&&card.IsVisible,"pause cancels checks without hiding the widget");
   Assert(!((Button)Field(card,"refresh")).IsEnabled&&Text(card,"freshnessText").Contains("paused"),"paused state explains disabled manual refresh");
   card.GetType().GetMethod("CompleteEstimate",Hidden).Invoke(card,new object[]{ticket,new CliResult{Map=new TaskMap{Low=90,High=99}},null,s});Assert(((TaskMap)Field(card,"map")).Low==20,"paused callback cannot overwrite current estimate");
   store.Settings.Paused=false;store.Settings.ManualOnly=true;card.GetType().GetMethod("ApplyPreferences",Hidden).Invoke(card,null);Assert(((Button)Field(card,"refresh")).IsEnabled&&Text(card,"freshnessText").Contains("Manual"),"manual-only keeps refresh available and explains mode");
   store.Settings.ManualOnly=false;store.Settings.ReducedMotion=true;store.Settings.TextScale=1.3;store.Settings.Accent="Purple";card.GetType().GetMethod("ApplyPreferences",Hidden).Invoke(card,null);card.GetType().GetField("busy",Hidden).SetValue(card,true);Observe(card,s);
   Assert(((FrameworkElement)Field(card,"updating")).Visibility==Visibility.Visible&&((FrameworkElement)Field(card,"loading")).Visibility==Visibility.Collapsed&&((FrameworkElement)Field(card,"sweep")).Visibility==Visibility.Collapsed,"reduced motion retains updating badge without animations");
   Assert(Math.Abs(((TextBlock)Field(card,"percent")).FontSize-44.2)<.01,"dynamic percentage respects text scale exactly once");
   store.Settings.ShowEta=false;store.Settings.ShowFreshness=false;card.GetType().GetMethod("ApplyPreferences",Hidden).Invoke(card,null);Assert(((FrameworkElement)Field(card,"etaText")).Visibility==Visibility.Collapsed&&((FrameworkElement)Field(card,"freshnessText")).Visibility==Visibility.Collapsed,"ETA and freshness can be independently hidden");
   card.GetType().GetField("busy",Hidden).SetValue(card,false);store.Settings.TextScale=1;store.Settings.FinishBehavior="Collapse";store.Settings.FinishDelaySeconds=0;
   s.Generating=false;s.FinalVisible=true;Observe(card,s);Thread.Sleep(1600);Observe(card,s);card.GetType().GetMethod("ApplyFinishedBehavior",Hidden).Invoke(card,new object[]{DateTime.UtcNow});
   Assert((bool)Field(card,"collapsed")&&Text(card,"compactPercent")=="100%","newly finished response auto-collapses with 100 percent");
   s.Prompt="Another task";s.Generating=true;s.FinalVisible=false;Observe(card,s);Assert(!(bool)Field(card,"collapsed"),"new task expands automatic collapse");
   store.Settings.FinishBehavior="Hide";s.Generating=false;s.FinalVisible=true;Observe(card,s);Thread.Sleep(1600);Observe(card,s);card.GetType().GetMethod("ApplyFinishedBehavior",Hidden).Invoke(card,new object[]{DateTime.UtcNow});
   Assert((bool)Field(card,"finishedHidden")&&!card.IsVisible,"finish hide is separate from explicit user hiding");card.GetType().GetMethod("ToggleWidget",Hidden).Invoke(card,null);Assert(card.IsVisible&&!(bool)Field(card,"finishedHidden"),"one shortcut press restores auto-hidden card");
   s.Prompt="Fresh task again";s.Generating=true;s.FinalVisible=false;Observe(card,s);Assert(!(bool)Field(card,"finishedHidden"),"new task clears finished hide");
   store.Settings.LockPosition=true;card.GetType().GetMethod("ResetPosition",Hidden).Invoke(card,null);Assert(!Double.IsNaN(card.Left)&&!Double.IsNaN(card.Top),"position reset works even with position lock");
   store.SaveSettings();var loaded=new Storage(store.Folder);Assert(loaded.Settings.LockPosition&&loaded.Settings.ReducedMotion&&loaded.Settings.Accent=="Purple"&&!loaded.Settings.ShowEta,"new settings roundtrip without losing preferences");
   card.GetType().GetMethod("ToggleDetails",Hidden).Invoke(card,null);((TextBlock)Field(card,"evidence")).Text=new string('x',4000);card.SizeToContent=SizeToContent.Manual;card.Height=250;card.UpdateLayout();
   var scroll=(ScrollViewer)Field(card,"bodyScroll");scroll.ApplyTemplate();var track=scroll.Template.FindName("PART_VerticalScrollBar",scroll) as System.Windows.Controls.Primitives.ScrollBar;var presenter=scroll.Template.FindName("PART_ScrollContentPresenter",scroll) as FrameworkElement;
   Assert(track!=null&&track.Visibility==Visibility.Visible&&track.ActualWidth>=12,"overflow uses slim dark scrollbar with reserved gutter");
   Assert(presenter!=null&&track!=null&&presenter.ActualWidth+track.ActualWidth<=scroll.ActualWidth+.5,"scrollbar cannot overlay content width");
   scroll.ScrollToBottom();card.UpdateLayout();Assert(scroll.VerticalOffset>0,"themed scrollbar retains functional scrolling");
  }finally{card.Close();}
 }
 [STAThread]static void Main(string[] args){
  if(args.Length==1&&args[0]=="--fixture-wait"){Thread.Sleep(1500);return;}
  var folder=Path.Combine(Path.GetTempPath(),"ProgressPeek-regression-"+Guid.NewGuid().ToString("N"));
  try{
   Assert(SetupTests.Run()==0,"migration and setup consent");TimingTests.Run(Assert);DiagnosticTests.Run(Assert);BugReportTests.Run(Assert);UiPreferenceTests.Run(Assert);UpdateTests.Run(Assert);
   CheckQualityOfLife(folder);
   ScopeFlowTests.Run(Assert,folder);
   ReaderFlowTests.Run(Assert);
   AnchoringTests.Run(Assert,folder);
   LayoutFlowTests.Run(Assert,folder);TokenFooterTests.Run(Assert,folder);PresentationTests.Run(Assert,folder);
   var store=new Storage(folder);store.Settings.Enabled=false;
   var card=new Card(store,true);
   try{
    var waiting=Scan(false);waiting.Output="";Observe(card,waiting);Assert(Text(card,"percent")=="0%","submitted prompt waiting for generation starts at zero");
    var scan=Scan(true);Observe(card,scan);Assert(Text(card,"percent")=="0%","generation beginning after pending prompt retains local zero");card.GetType().GetField("map",Hidden).SetValue(card,Map());Observe(card,scan);
    Assert(Text(card,"percent")=="20–30%","real card shows estimate");
    scan.ChatIdentity="runtime-2";Observe(card,scan);
    Assert(Text(card,"percent")=="20–30%","runtime-id churn does not reset submitted task");
    Observe(card,new Snapshot{Error="Chat layout changed"});
    Assert(Text(card,"percent")=="20–30%","temporary accessibility failure keeps prior estimate");
    Assert(((Snapshot)Field(card,"current")).Handle==new IntPtr(123),"temporary failure keeps window anchor");
    var details=Field(card,"details") as FrameworkElement;
    Assert(details!=null&&details.Visibility==Visibility.Collapsed,"details collapsed by default");
    var toggle=card.GetType().GetMethod("ToggleDetails",Hidden);
    if(toggle!=null){toggle.Invoke(card,null);Assert(details.Visibility==Visibility.Visible,"details expands on press");toggle.Invoke(card,null);Assert(details.Visibility==Visibility.Collapsed,"details collapses on second press");}
    else Assert(false,"details toggle exists");
    var refresh=Field(card,"refresh") as Button;Assert(refresh!=null,"manual refresh control exists");
    card.GetType().GetField("busy",Hidden).SetValue(card,true);((CheckState)Field(card,"checkStatus")).Begin();Observe(card,scan);
    var updating=Field(card,"updating") as FrameworkElement;var loading=Field(card,"loading") as ProgressBar;
    Assert(updating!=null&&updating.Visibility==Visibility.Visible,"busy badge is prominent alongside estimate");
    Assert(loading!=null&&loading.Visibility==Visibility.Visible&&loading.IsIndeterminate,"updating has indeterminate strip");
    Assert(refresh!=null&&!refresh.IsEnabled,"busy check prevents duplicate refresh");
    card.GetType().GetField("busy",Hidden).SetValue(card,false);((CheckState)Field(card,"checkStatus")).Complete();
    scan.Generating=false;Set(scan,"FinalVisible",false);Observe(card,scan);Thread.Sleep(1700);Observe(card,scan);
    Assert(Text(card,"percent")!="100%","missing stop without final evidence cannot certify completion");
    int beforeFinish=((Epoch)Field(card,"epoch")).Current;Set(scan,"FinalVisible",true);Observe(card,scan);Thread.Sleep(1700);Observe(card,scan);
    Assert(Text(card,"percent")=="100%","normal ended response reaches 100 percent");
    var callback=card.GetType().GetMethod("CompleteEstimate",Hidden);Assert(callback!=null,"estimator completion callback test seam exists");if(callback!=null)callback.Invoke(card,new object[]{beforeFinish,new CliResult{Map=new TaskMap{Low=0,High=5,Confidence="low"}},null,scan});Observe(card,scan);
    Assert(Text(card,"percent")=="100%","late lower estimate cannot replace completion");
    Assert(((TaskMap)Field(card,"map")).Low==20,"late estimator callback does not mutate completed task map");
    scan.Generating=true;scan.FinalVisible=false;Observe(card,scan);Assert(Text(card,"percent")=="0%"&&Field(card,"map")==null,"new response to identical prompt cannot restore previous estimate");
    scan.Prompt="New submitted task";scan.Generating=true;Set(scan,"FinalVisible",false);Observe(card,scan);
    Assert(Text(card,"percent")!="100%","new submitted task resets completion");
    Assert(Text(card,"percent")=="0%","new submitted prompt displays zero without Luna");
    card.GetType().GetField("busy",Hidden).SetValue(card,true);((CheckState)Field(card,"checkStatus")).Begin();var epoch=(Epoch)Field(card,"epoch");int beforeCancel=epoch.Current;
    scan.Generating=false;Set(scan,"Terminal","Cancelled");Set(scan,"FinalVisible",true);Observe(card,scan);Thread.Sleep(1700);Observe(card,scan);
    Assert(Text(card,"percent")!="100%","cancelled response never forced to 100");
    Assert(!(bool)Field(card,"busy")&&!epoch.IsCurrent(beforeCancel),"terminal entry invalidates in-flight estimator results");
    if(callback!=null)callback.Invoke(card,new object[]{beforeCancel,new CliResult{Map=Map()},null,scan});Assert(Field(card,"map")==null,"cancelled task rejects late actual callback");
    Set(scan,"Terminal","");Observe(card,scan);Thread.Sleep(1700);Observe(card,scan);
    Assert(Text(card,"percent")!="100%","disappearing cancellation label cannot become normal completion");
    scan.Generating=true;Set(scan,"FinalVisible",false);Observe(card,scan);Assert(((Lifecycle)Field(card,"lifecycle")).Terminal=="","resumed generation clears terminal state");
    scan.Generating=false;scan.Terminal="Awaiting your input";Observe(card,scan);scan.Terminal="";scan.FinalVisible=true;Observe(card,scan);Thread.Sleep(1600);Observe(card,scan);Assert(Text(card,"percent")!="100%","input wait remains paused when label disappears");
    store.Settings.Enabled=true;int calls=store.Ledger.DailyCalls;card.GetType().GetMethod("ManualRefresh",Hidden).Invoke(card,null);Assert(store.Ledger.DailyCalls==calls&&Text(card,"hint").StartsWith("Demo only"),"demo refresh cannot bill or scan other chats");store.Settings.Enabled=false;
    Observe(card,new Snapshot{Handle=new IntPtr(123),Chat="Empty chat",ChatIdentity="empty",Error="Waiting for an accessible user message"});
    Assert(Text(card,"percent")!="—","missing message uses readable empty state");
    Assert(store.Settings.ShowUsage&&!store.Settings.LockSize&&store.Settings.HotkeyEnabled,"usage and resize enabled by default");
    CheckDragging(card,store);
    card.Show();card.UpdateLayout();var noDragGrip=(System.Windows.Controls.Primitives.Thumb)Field(card,"resizeGrip");noDragGrip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});Assert(new Storage(folder).Settings.WidgetHeight==0&&!File.ReadAllText(Path.Combine(folder,"settings.json")).Contains("NaN"),"resize click without dragging keeps finite automatic-size settings");double beforeWidth=card.ActualWidth;card.GetType().GetMethod("ResizeCard",Hidden).Invoke(card,new object[]{150d,80d});card.UpdateLayout();Assert(card.Width>beforeWidth&&card.Height>=240,"real card resizes in both dimensions");
    var grip=(System.Windows.Controls.Primitives.Thumb)Field(card,"resizeGrip");grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(150,80,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});var sized=new Storage(folder);Assert(sized.Settings.WidgetWidth==card.Width&&sized.Settings.WidgetHeight==card.Height,"resized dimensions persist");
    store.Settings.LockSize=true;card.GetType().GetMethod("ApplySizeLock",Hidden).Invoke(card,null);double lockedWidth=card.Width;card.GetType().GetMethod("ResizeCard",Hidden).Invoke(card,new object[]{100d,100d});Assert(grip.Visibility==Visibility.Collapsed&&!grip.IsEnabled&&card.Width==lockedWidth,"size lock removes grip and prevents resize");
    Observe(card,Scan(true));card.GetType().GetField("map",Hidden).SetValue(card,Map());Observe(card,Scan(true));card.UpdateLayout();var fill=(System.Windows.Controls.Border)Field(card,"bar");Assert(Math.Abs(fill.Width-((FrameworkElement)fill.Parent).ActualWidth*.30)<1,"progress bar scales with resized width");
    ((TaskMap)Field(card,"map")).Confidence="medium";Observe(card,Scan(true));Assert(Text(card,"confidence").Contains("medium confidence"),"UI does not override inferred confidence");
    var visibilityToggle=card.GetType().GetMethod("ToggleWidget",Hidden);visibilityToggle.Invoke(card,null);Assert(!card.IsVisible&&(bool)Field(card,"userHidden"),"visibility toggle hides card");visibilityToggle.Invoke(card,null);Assert(card.IsVisible&&!(bool)Field(card,"userHidden"),"visibility toggle restores card");
    store.Settings.ShowUsage=false;store.Settings.TokenDisplay="Hidden";card.GetType().GetMethod("StartUsage",Hidden).Invoke(card,null);Assert(((TextBlock)Field(card,"usageText")).Visibility==Visibility.Collapsed,"footer hides with plan usage and widget tokens both disabled");
   }finally{card.Close();}
   var previous=Map();string json="{\"phase\":\"Checking\",\"confidence\":\"low\",\"low\":30,\"high\":40,\"inferred\":true,\"explanation\":\"Tests running\",\"steps\":[{\"id\":\"code\",\"label\":\"Implementation and verification\",\"weight\":90,\"state\":\"active\",\"evidence\":\"Tests running\"}]}";
   foreach(string malformed in new[]{json.Replace("\"low\":30","\"low\":30.2"),json.Replace("\"low\":30","\"low\":\"30\""),json.Replace("\"inferred\":true","\"inferred\":1"),json.Replace("\"weight\":90","\"weight\":true"),json.Replace("\"evidence\":\"Tests running\"","\"evidence\":null")}){bool rejected=false;try{MapCodec.Parse(malformed,null,"");}catch{rejected=true;}Assert(rejected,"malformed JSON field types rejected");}
   var a=Scan(true);var b=Scan(true);Set(a,"PanelIdentity","left-chat-panel");Set(b,"PanelIdentity","right-chat-panel");Assert(Lifecycle.TaskKey(a)!=Lifecycle.TaskKey(b),"same-titled panels have separate task identities");
   try{var updated=MapCodec.Parse(json,previous,"Tests running");Assert(updated.Low==30&&updated.Steps[0].Label=="Code and verify"&&updated.Steps[0].Weight==100,"cosmetic semantic changes normalized rather than rejected");}catch{Assert(false,"cosmetic semantic changes normalized rather than rejected");}
   Assert(MessageBoundary.IsRunning(new[]{new MessageNode("Text","Thinking")}),"visible thinking is active work");
   var ending=typeof(MessageBoundary).GetMethod("IsFinished");Assert(ending!=null&&(bool)ending.Invoke(null,new object[]{new[]{new MessageNode("Button","Worked for 27s")}}),"real Worked for button confirms finished response");
   Assert(MessageBoundary.FindPromptEnd(new[]{new MessageNode("Text","You said:"),new MessageNode("Group","Attachment"),new MessageNode("Text","Fix the attached issue"),new MessageNode("Text","ChatGPT said:")},0)==2,"attachment wrapper cannot hide submitted prompt");
   var parents=new Dictionary<string,string>{{"draft-edit","editor-wrapper"},{"editor-wrapper","composer-with-unsent-attachment"},{"composer-with-unsent-attachment","conversation"}};string branch=MessageBoundary.ComposerBranch("draft-edit","conversation",n=>parents.ContainsKey(n)?parents[n]:null,(x,y)=>x==y);Assert(branch=="composer-with-unsent-attachment","nested draft and unsent attachment branch excluded together");Assert(!MessageBoundary.IsStopControl(new MessageNode("Button","Approve"))&&MessageBoundary.IsStopControl(new MessageNode("Button","Stop")),"old approval is not composer running/terminal evidence");
   var reserve=typeof(Storage).GetMethod("Reserve",new[]{typeof(string),typeof(string),typeof(bool),typeof(DateTime),typeof(int).MakeByRefType(),typeof(string).MakeByRefType()});
   uint modifier,keyCode;Assert(DesktopPolicy.ParseHotkey("Ctrl+Alt+P",out modifier,out keyCode)&&modifier==3&&keyCode==80,"default shortcut parsed");Assert(!DesktopPolicy.ParseHotkey("P",out modifier,out keyCode)&&!DesktopPolicy.ParseHotkey("Ctrl+Ctrl+P",out modifier,out keyCode),"unsafe or malformed shortcut rejected");
   using(var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("ProgressPeek shortcut test"){Width=1,Height=1,WindowStyle=0}))using(var otherSource=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("ProgressPeek conflict test"){Width=1,Height=1,WindowStyle=0})){int toggles=0;using(var binding=new HotkeyBinding(source,()=>toggles++))using(var other=new HotkeyBinding(otherSource,()=>{})){string problem;Assert(binding.Set(true,"Ctrl+Alt+F24",out problem),"Windows registers global shortcut");Assert(!other.Set(true,"Ctrl+Alt+F24",out problem)&&problem.Contains("in use"),"shortcut conflicts detected without replacing existing binding");SendMessage(source.Handle,0x0312,new IntPtr(0x621),IntPtr.Zero);Assert(toggles==1,"registered hotkey message invokes toggle");binding.Set(false,"",out problem);SendMessage(source.Handle,0x0312,new IntPtr(0x621),IntPtr.Zero);Assert(toggles==1,"disabled shortcut ignores subsequent messages");}}
   Assert(DesktopPolicy.ShouldShow(false,true,true,false)&&!DesktopPolicy.ShouldShow(false,true,true,true)&&!DesktopPolicy.ShouldShow(false,false,false,false),"visibility depends on window existence and minimization, not foreground app");
   var fitted=DesktopPolicy.FitSize(800,1000,new Size(640,600));Assert(fitted.Width==640&&fitted.Height==600,"large saved dimensions fit smaller monitor work area");
   var paused=new Lifecycle();var pauseScan=Scan(true);paused.Update(pauseScan,DateTime.UtcNow);pauseScan=Scan(false);pauseScan.Terminal="Awaiting your input";paused.Update(pauseScan,DateTime.UtcNow);Assert(!paused.Update(Scan(true),DateTime.UtcNow)&&paused.Terminal=="","resuming approval wait preserves current task identity");
   var usageFixture=UsageReader.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":7,\"windowDurationMins\":10080},\"secondary\":null}}",DateTime.UtcNow);Assert(UsageFooter.Format(usageFixture,DateTime.UtcNow,false)=="Weekly · 93% left","weekly-only footer has no fabricated five-hour limit");Assert(UsageFooter.Format(usageFixture,DateTime.UtcNow.AddMinutes(11),false).Contains("stale"),"old usage snapshot clearly marked stale");usageFixture.Windows[0].ResetsAt=0;Assert(UsageFooter.Format(usageFixture,DateTime.UtcNow,false).Contains("stale"),"expired quota not shown as current");
   var longStore=new Storage(Path.Combine(folder,"long-task"));longStore.Settings.EnforceDailyLimit=false;longStore.Settings.EnforceTaskLimit=false;int accepted=0,callCount;string why;var clock=new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc);for(int i=0;i<120;i++)if(longStore.Reserve("hour-task",i.ToString(),false,clock.AddSeconds(i*30),out callCount,out why))accepted++;Assert(accepted==120&&new Storage(longStore.Folder).Ledger.DailyCalls==120,"one-hour task receives all 120 spaced updates with caps off");
   Assert(reserve!=null,"manual/automatic reservation interface exists");
   if(reserve!=null){
    store.Settings.EnforceDailyLimit=false;Set(store.Settings,"EnforceTaskLimit",false);var now=DateTime.UtcNow;
    Func<string,string,bool,DateTime,bool> take=(task,hash,manual,time)=>{object[] invokeArgs={task,hash,manual,time,0,""};return (bool)reserve.Invoke(store,invokeArgs);};
    Assert(take("live","one",false,now),"first live check counted");
    Assert(!take("live","two",false,now.AddSeconds(29)),"automatic interval enforced");
    Assert(take("live","two",false,now.AddSeconds(30)),"live check allowed after 30 seconds");
    Assert(take("live","two",true,now.AddSeconds(40)),"manual allows unchanged evidence");
    Assert(!take("live","two",true,now.AddSeconds(45)),"manual click debounce enforced");
    Assert(take("live","two",true,now.AddSeconds(50)),"manual bypasses automatic task ceiling");
    Assert(store.Ledger.DailyCalls==4,"all checks counted with optional caps off");
    Set(store.Settings,"EnforceTaskLimit",true);Assert(!take("live","three",false,now.AddSeconds(90)),"enabled automatic task ceiling applies");
    store.Settings.EnforceDailyLimit=true;store.Settings.DailyChecks=4;Assert(!take("live","three",true,now.AddSeconds(90)),"manual respects daily allowance setting");
    store.SaveSettings();var restored=new Storage(folder);Assert(restored.Ledger.DailyCalls==4,"new settings never erase usage ledger");
    var upgrade=typeof(Storage).GetMethod("UpgradeLiveDefaults");Assert(upgrade!=null,"approved live settings migration exists");
    if(upgrade!=null){upgrade.Invoke(restored,null);Assert(!restored.Settings.EnforceTaskLimit&&restored.Settings.AutoIntervalSeconds==30&&restored.Ledger.DailyCalls==4&&restored.Settings.EnforceDailyLimit,"migration retains daily setting and counters while enabling live mode");restored.Settings.AutoIntervalSeconds=60;restored.SaveSettings();upgrade.Invoke(restored,null);Assert(restored.Settings.AutoIntervalSeconds==60,"migration never overwrites later user preferences");}
   }
  }finally{Directory.Delete(folder,true);}
  if(args.Length>0&&args[0]=="--render")RenderStates(args[1]);
  if(args.Length>0&&args[0]=="--render-beta")RenderBeta(args[1]);
  Console.WriteLine((failures==0?"PASS ":"FAILED ")+checks+" regression assertions; failures="+failures);Environment.ExitCode=failures==0?0:1;
 }
 static void SaveVisual(FrameworkElement visual,string path){visual.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
 static T FindVisual<T>(DependencyObject node) where T:DependencyObject {var value=node as T;if(value!=null)return value;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++){var found=FindVisual<T>(System.Windows.Media.VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
 static void RenderBeta(string folder){
  Directory.CreateDirectory(folder);var temp=Path.Combine(Path.GetTempPath(),"ProgressPeek-beta-render-"+Guid.NewGuid().ToString("N"));
  try{var store=new Storage(temp);store.Settings.Enabled=false;var card=new Card(store,true);try{
   var scan=Scan(true);Observe(card,scan);card.GetType().GetField("map",Hidden).SetValue(card,Map());Observe(card,scan);card.Show();card.UpdateLayout();SaveVisual(card,Path.Combine(folder,"normal.png"));
   card.GetType().GetMethod("ToggleCollapse",Hidden).Invoke(card,null);SaveVisual(card,Path.Combine(folder,"collapsed.png"));card.GetType().GetMethod("ToggleCollapse",Hidden).Invoke(card,null);
   store.Settings.Accent="Purple";store.Settings.TextScale=1.3;card.GetType().GetMethod("ApplyPreferences",Hidden).Invoke(card,null);card.GetType().GetMethod("ToggleDetails",Hidden).Invoke(card,null);card.SizeToContent=SizeToContent.Manual;card.Height=330;SaveVisual(card,Path.Combine(folder,"large-text-scroll.png"));
   card.GetType().GetMethod("ShowSettings",Hidden).Invoke(card,null);var settings=(Window)Field(card,"settingsWindow");settings.UpdateLayout();var tabs=FindVisual<TabControl>(settings);
   for(int i=0;i<tabs.Items.Count;i++){tabs.SelectedIndex=i;settings.UpdateLayout();SaveVisual(settings,Path.Combine(folder,"settings-"+i+".png"));}
   settings.Close();card.ContextMenu.PlacementTarget=card;card.ContextMenu.IsOpen=true;card.ContextMenu.UpdateLayout();SaveVisual(card.ContextMenu,Path.Combine(folder,"menu.png"));card.ContextMenu.IsOpen=false;
  }finally{card.Close();}}finally{Directory.Delete(temp,true);}
 }
 static void RenderStates(string folder){Directory.CreateDirectory(folder);var temp=Path.Combine(Path.GetTempPath(),"ProgressPeek-render-"+Guid.NewGuid().ToString("N"));try{var store=new Storage(temp);store.Settings.Enabled=false;foreach(string state in new[]{"compact","updating","expanded","finished","resized","locked","start"}){store.Settings.LockSize=state=="locked";var card=new Card(store,true);try{var s=Scan(true);Observe(card,s);card.GetType().GetField("map",Hidden).SetValue(card,Map());Observe(card,s);if(state=="updating"){card.GetType().GetField("busy",Hidden).SetValue(card,true);((CheckState)Field(card,"checkStatus")).Begin();Observe(card,s);}if(state=="expanded")card.GetType().GetMethod("ToggleDetails",Hidden).Invoke(card,null);if(state=="finished"){s.Generating=false;s.FinalVisible=true;Observe(card,s);Thread.Sleep(1600);Observe(card,s);}if(state=="resized"||state=="locked"){card.SizeToContent=SizeToContent.Manual;card.Width=480;card.Height=410;card.GetType().GetMethod("ToggleDetails",Hidden).Invoke(card,null);}if(state=="start"){s.Prompt="Fresh prompt";s.Output="";Observe(card,s);}card.Show();card.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth),(int)Math.Ceiling(card.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(card);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(folder,"ProgressPeek-"+state+".png")))encoder.Save(file);}finally{card.Close();}}}finally{Directory.Delete(temp,true);}}
}
