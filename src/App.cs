using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace ProgressPeek {
 public partial class Card:Window {
  readonly Storage store;readonly bool demo;readonly StackPanel body,details;readonly TextBlock phase,percent,confidence,evidence,foot,hint,updating;readonly Border bar,sweep;readonly ProgressBar loading;readonly Button refresh,detailButton;readonly Forms.NotifyIcon tray;readonly CliEstimator estimator;readonly Epoch epoch=new Epoch();readonly Lifecycle lifecycle=new Lifecycle();
  TokenTally taskTokens=new TokenTally();readonly SessionCache sessions=new SessionCache();readonly TaskScope scopes=new TaskScope();
  CheckState checkStatus=new CheckState();
  string reservationTask="",reservationHash="",requestOutcome="";DateTime lastScan=DateTime.MinValue;
  Monitor monitor;Snapshot current,latest;TaskMap map;CancellationTokenSource cancel=new CancellationTokenSource();string taskKey="",cacheKey="";bool initialized,busy,collapsed,detailsExpanded,suppressAuto;int checks;long inputTokens,outputTokens;IntPtr hwnd,settingsHandle;DispatcherTimer follow;DateTime lastGood=DateTime.MinValue,lastEstimate=DateTime.MinValue;
  static SolidColorBrush Color(string hex){return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));}
  static TextBlock Text(string value,double size,string color){return new TextBlock{Text=value,FontSize=size,Foreground=Color(color),TextWrapping=TextWrapping.Wrap};}
  static Button Control(object content,string name){var b=new Button{Content=content,ToolTip=name,Padding=new Thickness(7,5,7,5),Foreground=Color("#CFD3DC"),Background=Color("#292C33"),BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand};System.Windows.Automation.AutomationProperties.SetName(b,name);var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Button.BackgroundProperty));var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);presenter.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(Button.PaddingProperty));border.AppendChild(presenter);b.Template=new ControlTemplate(typeof(Button)){VisualTree=border};b.MouseEnter+=(s,e)=>b.Background=Color("#363A44");b.MouseLeave+=(s,e)=>b.Background=Color("#292C33");return b;}
  public Card(Storage store,bool demo){this.store=store;this.demo=demo;estimator=new CliEstimator(store);Width=320;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;FontFamily=new FontFamily("Segoe UI");
   var shell=new Border{CornerRadius=new CornerRadius(15),Background=Color("#202126"),BorderBrush=Color("#44474F"),BorderThickness=new Thickness(1),Padding=new Thickness(19,16,19,16)};Content=shell;var root=new StackPanel();shell.Child=root;
   var header=new Grid{Background=Brushes.Transparent};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var title=Text(demo?"ProgressPeek · Demo":"ProgressPeek",16,"#F1F2F5");title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(title);
   var refreshIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 15,5 A 7,7 0 1 0 16,12 M 15,1 L 15,5 L 11,5"),Stroke=Color("#CFD3DC"),StrokeThickness=1.6,Width=18,Height=18,Stretch=Stretch.Uniform,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};refresh=Control(refreshIcon,"Refresh estimate using ChatGPT plan");refresh.Margin=new Thickness(6,0,0,0);refresh.Click+=(s,e)=>ManualRefresh();Grid.SetColumn(refresh,1);header.Children.Add(refresh);
   var menu=Control("···","Widget menu");menu.FontSize=18;menu.Margin=new Thickness(5,0,0,0);Grid.SetColumn(menu,2);header.Children.Add(menu);root.Children.Add(header);
   body=new StackPanel{Margin=new Thickness(0,14,0,0)};root.Children.Add(body);phase=Text("●  Waiting for a task",12,"#89ABFF");phase.MaxHeight=34;phase.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(phase);
   var numberRow=new Grid{Margin=new Thickness(0,8,0,10)};numberRow.ColumnDefinitions.Add(new ColumnDefinition());numberRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});percent=Text("Awaiting message",22,"#F4F5F8");percent.FontWeight=FontWeights.SemiBold;percent.VerticalAlignment=VerticalAlignment.Center;numberRow.Children.Add(percent);updating=Text("Updating…",12,"#AFC8FF");updating.FontWeight=FontWeights.SemiBold;updating.VerticalAlignment=VerticalAlignment.Center;updating.Visibility=Visibility.Collapsed;updating.Margin=new Thickness(7,0,0,0);Grid.SetColumn(updating,1);numberRow.Children.Add(updating);body.Children.Add(numberRow);
   var track=new Border{Height=8,CornerRadius=new CornerRadius(4),Background=Color("#373A43")};var grid=new Grid();track.Child=grid;bar=new Border{Height=8,CornerRadius=new CornerRadius(4),Background=Color("#759DFA"),Width=0,HorizontalAlignment=HorizontalAlignment.Left};grid.Children.Add(bar);loading=new ProgressBar{Height=8,IsIndeterminate=true,Foreground=Color("#759DFA"),Background=Color("#373A43"),BorderThickness=new Thickness(0),Visibility=Visibility.Collapsed};System.Windows.Automation.AutomationProperties.SetName(loading,"Updating estimate");grid.Children.Add(loading);sweep=new Border{Height=8,Width=75,CornerRadius=new CornerRadius(4),HorizontalAlignment=HorizontalAlignment.Left,Background=Color("#89ABFF"),RenderTransform=new TranslateTransform(),Visibility=Visibility.Collapsed,IsHitTestVisible=false};grid.Children.Add(sweep);body.Children.Add(track);
   hint=Text("Waiting for a submitted prompt",11,"#ADB3BF");hint.Margin=new Thickness(0,12,0,0);hint.MaxHeight=30;body.Children.Add(hint);
   detailButton=Control("Details","Show or hide estimate details");detailButton.HorizontalAlignment=HorizontalAlignment.Left;detailButton.Margin=new Thickness(0,10,0,0);detailButton.Click+=(s,e)=>ToggleDetails();body.Children.Add(detailButton);
   details=new StackPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,10,0,0)};confidence=Text("Progress unavailable",11,"#ADB3BF");details.Children.Add(confidence);evidence=Text("Actual work evidence is needed for an estimate.",12,"#C4C8D0");evidence.Margin=new Thickness(0,8,0,0);details.Children.Add(evidence);foot=Text("ChatGPT plan · 0 checks",10,"#ADB3BF");foot.Margin=new Thickness(0,12,0,0);details.Children.Add(foot);body.Children.Add(details);
   InitializeDesktop(shell,root);
   var context=new ContextMenu();Add(context,"Settings",()=>ShowSettings());Add(context,"Setup & privacy",()=>ShowSetup());Add(context,"Report a bug",()=>ShowBugReport(this));Add(context,"Refresh estimate",()=>ManualRefresh());Add(context,"Show / hide widget",()=>ToggleWidget());Add(context,"Collapse / expand",()=>ToggleCollapse());Add(context,"Enable / disable click-through",()=>{store.Settings.ClickThrough=!store.Settings.ClickThrough;store.SaveSettings();ApplyClickThrough();});Add(context,"Exit",()=>Application.Current.Shutdown());ContextMenu=context;menu.Click+=(s,e)=>{context.PlacementTarget=menu;context.IsOpen=true;};InitializeDragging(shell,header);
   tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="ProgressPeek · right-click for settings / exit",Visible=!demo};var trayMenu=new Forms.ContextMenuStrip();trayMenu.Items.Add("Show / hide widget",null,(s,e)=>Dispatcher.BeginInvoke(new Action(ToggleWidget)));trayMenu.Items.Add("Settings",null,(s,e)=>Dispatcher.BeginInvoke(new Action(ShowSettings)));trayMenu.Items.Add("Report a bug",null,(s,e)=>Dispatcher.BeginInvoke(new Action(()=>ShowBugReport(this))));trayMenu.Items.Add("Recover card (turn off click-through)",null,(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{store.Settings.ClickThrough=false;store.SaveSettings();ApplyClickThrough();})));trayMenu.Items.Add("Exit",null,(s,e)=>Dispatcher.BeginInvoke(new Action(()=>Application.Current.Shutdown())));tray.ContextMenuStrip=trayMenu;tray.DoubleClick+=(s,e)=>Dispatcher.BeginInvoke(new Action(ShowSettings));
   trayNotifications=new TrayNotifications(tray);InitializeUpdates();SourceInitialized+=(s,e)=>{hwnd=new WindowInteropHelper(this).Handle;int style=Native.GetWindowLong(hwnd,-20);Native.SetWindowLong(hwnd,-20,style|0x08000000|0x80);ApplyClickThrough();};Closed+=(s,e)=>{StopUpdates();cancel.Cancel();if(monitor!=null)monitor.Dispose();if(follow!=null)follow.Stop();tray.Dispose();};
   InitializeQualityOfLife(header,title);PreviewMouseDown+=(s,e)=>{if(settingsWindow!=null)settingsWindow.Close();};Loaded+=(s,e)=>{ApplySizeLock();if(SizeToContent!=SizeToContent.Manual)FitContentHeight();};
   if(demo){Left=150;Top=150;phase.Text="●  Coding and running tools";percent.FontSize=34;percent.Text="40–50%";bar.Width=140;hint.Text="Approximate progress";confidence.Text="Inferred estimate · low confidence";evidence.Text="Core behavior implemented; verification is underway.";foot.Text="Demo · simulated evidence · no model calls";}else{monitor=new Monitor(s=>Dispatcher.BeginInvoke(new Action(()=>Observe(s))));follow=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};follow.Tick+=(s,e)=>{ApplyFinishedBehavior(DateTime.UtcNow);UpdateQualityLabels(DateTime.UtcNow);Follow();};follow.Start();}
  }
  void Add(ContextMenu m,string name,Action action){var item=new MenuItem{Header=name};item.Click+=(s,e)=>action();m.Items.Add(item);}
  void ToggleCollapse(){SetCollapsed(!collapsed,false);}
  void ToggleDetails(){detailsExpanded=!detailsExpanded;details.Visibility=detailsExpanded?Visibility.Visible:Visibility.Collapsed;detailButton.Content=detailsExpanded?"Hide details":"Details";FitContentHeight();}
  void ApplyClickThrough(){if(hwnd==IntPtr.Zero)return;int style=Native.GetWindowLong(hwnd,-20);Native.SetWindowLong(hwnd,-20,store.Settings.ClickThrough?style|0x20:style&~0x20);}
  void SaveOffset(){if(current==null)return;Native.RECT rect;if(!Native.GetWindowRect(current.Handle,out rect))return;var source=PresentationSource.FromVisual(this);var scale=source==null?1:source.CompositionTarget.TransformFromDevice.M11;store.Settings.OffsetX=Left-rect.Right*scale;store.Settings.OffsetY=Top-rect.Top*scale;store.SaveSettings();}
  void ResetCheck(){if(reservationTask.Length>0){store.CompleteReservation(reservationTask,reservationHash,"transient",DateTime.UtcNow);reservationTask=reservationHash="";}epoch.Advance();cancel.Cancel();cancel.Dispose();cancel=new CancellationTokenSource();busy=false;}
  static string ScanHash(Snapshot s){return Evidence.Hash(s.Prompt+"\n"+s.FollowUps+"\n"+s.Context+"\n"+s.Output+"\n"+s.Activity);}
  void Observe(Snapshot s){
   lastScan=DateTime.UtcNow;
   s=scopes.Normalize(s,DateTime.UtcNow);latest=s;bool wasFinished=lifecycle.Finished;string wasTerminal=lifecycle.Terminal;bool changed=lifecycle.Update(s,DateTime.UtcNow);current=lifecycle.LastValid??s;bool reading=!String.IsNullOrEmpty(s.Error);
   if(lifecycle.Finished||lifecycle.Terminal.Length>0&&lifecycle.Terminal!="Awaiting your input")scopes.End(current);
   if(!reading)lastGood=DateTime.UtcNow;
   if(!initialized||changed){
    sessions.Remember(cacheKey,new SessionState{Tokens=taskTokens,Map=map,Checks=checks,InputTokens=inputTokens,OutputTokens=outputTokens,LastEstimateUtc=lastEstimate});initialized=true;taskKey=lifecycle.Key;cacheKey=Lifecycle.TaskKey(current);ResetCheck();if(lifecycle.Started)sessions.Forget(cacheKey);var saved=sessions.Find(cacheKey);taskTokens=saved==null?new TokenTally():saved.Tokens;map=saved==null?null:saved.Map;checks=saved==null?0:saved.Checks;inputTokens=saved==null?0:saved.InputTokens;outputTokens=saved==null?0:saved.OutputTokens;lastEstimate=saved==null?DateTime.MinValue:saved.LastEstimateUtc;checkStatus=new CheckState();
   }
   if(lifecycle.Finished&&!wasFinished){if(busy)RecordCheck("cancelled_response_finished",null,null);ResetCheck();checkStatus.Complete();}
   if(lifecycle.Terminal.Length>0&&lifecycle.Terminal!=wasTerminal){if(busy)RecordCheck("cancelled_response_terminal",null,null);ResetCheck();checkStatus.Complete();}
   var p=map==null?new Estimate():Estimate.FromMap(map,current.Output);percent.FontSize=p.Known?34:22;percent.Opacity=busy?0.45:1;bar.Opacity=busy?0.35:1;displayHigh=p.Known?p.High:0;UpdateBarWidth();
   percent.Text=p.Known?(p.Low==p.High?p.Low+"%":p.Low+"–"+p.High+"%"):(lifecycle.Started?"0%":current.Generating?"Estimating…":"Awaiting message");if(lifecycle.Started&&!p.Known)percent.FontSize=34;
   phase.Text="●  "+(current.Generating?(String.IsNullOrWhiteSpace(current.Activity)?"Working":current.Activity):"Awaiting response");
   confidence.Text=p.Known?"Evidence assessment · "+map.Confidence+" confidence\nModel assessment, not measured accuracy.":"No estimate yet";
   evidence.Text=p.Known?(map.Phase+": "+p.EvidenceText):(store.Settings.Enabled?"Visible task evidence is needed. Refresh to rescan the selected chat.":"Enable ChatGPT-plan estimates in Settings.");
   hint.Text=p.Known?"Approximate progress":lifecycle.Started?"Started · awaiting first estimate":current.Generating?"Visible work is being observed":"Waiting for a submitted message";
   if(!String.IsNullOrEmpty(lifecycle.Terminal)){phase.Text="●  "+lifecycle.Terminal;hint.Text="Response not completed";if(!p.Known)percent.Text=lifecycle.Terminal=="Awaiting your input"?"Awaiting input":lifecycle.Terminal;}
   if(reading){phase.Text="●  "+(map==null&&!s.Generating?"Awaiting message":(DateTime.UtcNow-lastGood).TotalSeconds>2?"Chat unavailable":"Reading chat…");hint.Text=p.Known?"Last estimate · waiting for chat access":"Waiting for an accessible message";percent.Opacity=0.55;evidence.Text=s.Error+". The last estimate is retained; missing accessibility is not completion.";if(!p.Known)percent.Text=s.Generating?"Reading chat…":"Awaiting message";}
   if(lifecycle.Finished){phase.Text="●  Response finished";percent.Text="100%";percent.FontSize=34;percent.Opacity=1;displayHigh=100;UpdateBarWidth();hint.Text="Current response completed";confidence.Text="Response ended · task success not verified";evidence.Text="100% means this prompt's response finished normally. It does not certify that the larger project succeeded.";}
   foot.Text="ChatGPT plan · "+checks+" checks"+(taskTokens.Total>0?" · ~"+taskTokens.Total.ToString("N0")+" raw tokens":"")+(current.Truncated?" · clipped":"")+(!store.Settings.EnforceDailyLimit?" · daily cap off":"")+(!store.Settings.EnforceTaskLimit?" · task cap off":"");
   if(!lifecycle.Finished&&!String.IsNullOrEmpty(checkStatus.Notice)){
    evidence.Text=checkStatus.Notice+(p.Known?"\n\nLast estimate: "+map.Phase+": "+p.EvidenceText:"");
    if(!String.IsNullOrEmpty(checkStatus.Error)){hint.Text="Check failed · press refresh";confidence.Text="Last estimate retained; latest check failed";}
    else if(checkStatus.Limit=="Daily check limit reached"||checkStatus.Limit=="Task check limit reached")hint.Text=checkStatus.Limit+" · Settings";
   }
   updating.Visibility=busy?Visibility.Visible:Visibility.Collapsed;loading.Visibility=busy&&!store.Settings.ReducedMotion?Visibility.Visible:Visibility.Collapsed;
   if(busy&&!store.Settings.ReducedMotion&&sweep.Visibility!=Visibility.Visible){sweep.Visibility=Visibility.Visible;AnimateSweep(true);}else if((!busy||store.Settings.ReducedMotion)&&sweep.Visibility!=Visibility.Collapsed){sweep.Visibility=Visibility.Collapsed;((TranslateTransform)sweep.RenderTransform).BeginAnimation(TranslateTransform.XProperty,null);}
   refresh.IsEnabled=!busy;refresh.Opacity=busy?0.45:1;
   if(busy)hint.Text=p.Known?"Last estimate · refreshing now":"Scanning visible task evidence";
   ApplyProgressTypography();
   QualityObservation(changed,reading,DateTime.UtcNow);
   if(contentFitted)FitContentHeight();
   // No automatic checks after ending, while idle, or merely because a draft is being typed.
   if(!demo&&!userHidden&&!store.Settings.Paused&&!store.Settings.ManualOnly&&!suppressAuto&&!reading&&store.Settings.Enabled&&!busy&&s.Generating&&!lifecycle.Finished&&lifecycle.Terminal.Length==0&&!String.IsNullOrWhiteSpace(s.Prompt)&&scopes.Ready(s,DateTime.UtcNow))StartEstimate(s,false);
  }
  void ApplyProgressTypography(){percent.FontSize=(percent.Text.Contains("%")?34:19)*WidgetTheme.ClampScale(store.Settings.TextScale);}
  void ManualRefresh(){
   if(demo){hint.Text="Demo only · no model calls";return;}if(busy||store.Settings.Paused)return;if(!SetupConsent.AllowsAi(store.Settings)){ShowSetup();checkStatus.Fail("Enable ChatGPT-plan estimates in Settings to refresh.");if(current!=null)Observe(current);return;}
   busy=true;checkStatus.Begin();int captured=epoch.Current;if(latest!=null)Observe(latest);
   Task.Run(()=>{Snapshot scan;try{scan=Monitor.Read();}catch{scan=new Snapshot{Error="Accessibility unavailable"};}Dispatcher.BeginInvoke(new Action(()=>{if(!epoch.IsCurrent(captured))return;busy=false;checkStatus.Complete();if(!AcceptManualScan(scan))return;if(!lifecycle.Finished&&!busy)StartEstimate(current,true);}));});
  }
  static bool ReadableEstimate(Snapshot s){return s!=null&&String.IsNullOrEmpty(s.Error)&&!String.IsNullOrWhiteSpace(s.Prompt)&&(!s.ProvenanceKnown||s.AtLatest);}
  bool AcceptManualScan(Snapshot scan){bool prior=suppressAuto;suppressAuto=true;try{Observe(scan);}finally{suppressAuto=prior;}if(ReadableEstimate(latest))return true;checkStatus.Fail("Cannot read the latest prompt yet. Keep the chat open and refresh again.");if(latest!=null)Observe(latest);return false;}
  void StartEstimate(Snapshot s,bool manual){
   if(!SetupConsent.AllowsAi(store.Settings)||!ReadableEstimate(s)||latest!=null&&!ReadableEstimate(latest)||demo||busy||store.Settings.Paused||!manual&&store.Settings.ManualOnly||lifecycle.Finished||lifecycle.Terminal.Length>0)return;string hash=ScanHash(s),reason;int count;string budgetKey=Evidence.Hash(s.Chat+"\n"+s.Prompt+"\n"+s.TaskIdentity);
   if(!store.Reserve(budgetKey,hash,manual,DateTime.UtcNow,CheckCadence.Interval(store.Settings,map),out count,out reason)){checks=count;checkStatus.Limit=reason;if(manual)hint.Text=reason;return;}
   checks=count;busy=true;checkStatus.Begin();reservationTask=budgetKey;reservationHash=hash;int captured=epoch.Current;var token=cancel.Token;var tally=taskTokens;var prior=map;RecordCheck("started",null,null,s);Observe(s);
   Task.Run(()=>{CliResult result=null;string error=null,outcome="success";
    try{result=estimator.Run(s.Prompt,"Visible status: "+s.Activity+"\n"+s.Output,s.Context,s.FollowUps,prior,token);}catch(OperationCanceledException){return;}catch(Exception ex){outcome=CheckOutcomePolicy.Failure(ex);error=CliEstimator.UserError(ex);if(outcome=="transient")error="Temporary estimate failure; automatic retries are limited.";var validation=ex as MapValidationException;if(validation!=null)result=new CliResult{InputTokens=validation.InputTokens,OutputTokens=validation.OutputTokens};}
    Dispatcher.BeginInvoke(new Action(()=>{RecordTokenUsage(s,result,tally);if(epoch.IsCurrent(captured))requestOutcome=outcome;CompleteEstimate(captured,result,error,s);}));
   });
  }
  void CompleteEstimate(int captured,CliResult result,string error,Snapshot scan){
   if(!epoch.IsCurrent(captured)||lifecycle.Finished||lifecycle.Terminal.Length>0){RenderUsage();return;}
   if(reservationTask.Length>0){store.CompleteReservation(reservationTask,reservationHash,latest!=null&&scan.FollowUps!=latest.FollowUps?"transient":String.IsNullOrEmpty(requestOutcome)?error==null?"success":"permanent":requestOutcome,DateTime.UtcNow);reservationTask=reservationHash=requestOutcome="";}
   if(latest!=null&&scan.FollowUps!=latest.FollowUps){RecordCheck("discarded_scope_changed",null,result,scan);busy=false;if(result!=null){inputTokens+=result.InputTokens;outputTokens+=result.OutputTokens;}checkStatus.Complete();Observe(latest);return;}
   RecordCheck(error==null?"completed":"failed",error,result,scan);busy=false;if(result!=null){inputTokens+=result.InputTokens;outputTokens+=result.OutputTokens;}if(error!=null)checkStatus.Fail(error);else{checkStatus.Complete();map=result.Map;lastEstimate=DateTime.UtcNow;var accepted=Estimate.FromMap(map,scan.Output);if(accepted.Known)eta.Add(accepted.Low,accepted.High,lastEstimate);}if(latest!=null)Observe(latest);
  }
  void RecordCheck(string state,string error,CliResult result,Snapshot snapshot=null){var scan=snapshot??current;try{File.WriteAllText(Path.Combine(store.Folder,"last-check.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{utc=DateTime.UtcNow,status=state,error=error,low=result==null||result.Map==null?-1:result.Map.Low,high=result==null||result.Map==null?-1:result.Map.High,input_tokens=result==null?0:result.InputTokens,output_tokens=result==null?0:result.OutputTokens,prompt_characters=scan==null?0:scan.Prompt.Length,response_characters=scan==null?0:scan.Output.Length,context_characters=scan==null?0:scan.Context.Length}));}catch{}}
 }
 public static class Entry {
  [STAThread]public static void Main(string[] args){
   try{if(UpdateInstaller.HandleHelper(args))return;if(args.Contains("--synthetic-check")){SyntheticCheck(args);return;}
    if(args.Contains("--diagnostic")){Snapshot s=null;Exception failure=null;var diagnosticThread=new Thread(()=>{try{s=Monitor.Read();}catch(Exception ex){failure=ex;}});diagnosticThread.SetApartmentState(ApartmentState.MTA);diagnosticThread.Start();if(!diagnosticThread.Join(12000)){Environment.ExitCode=1;return;}if(failure!=null)throw failure;var metadata=new{app_found=s.Handle!=IntPtr.Zero,chat_title_characters=s.Chat.Length,prompt_characters=s.Prompt.Length,response_characters=s.Output.Length,generating=s.Generating,nodes=s.NodeCount,truncated=s.Truncated,error=s.Error};string json=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(metadata);int index=Array.IndexOf(args,"--diagnostic");if(index+1<args.Length)File.WriteAllText(args[index+1],json);return;}
    if(args.Length==0&&UpdateInstaller.TryScheduledLaunch())return;
    var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};bool demo=args.Contains("--demo")||args.Contains("--render-demo");using(var mutex=new Mutex(true,"Local\\TaskProgressOverlay",out demoMutexCreated)){if(!demoMutexCreated&&!demo){MessageBox.Show("ProgressPeek is already running. Open its tray menu for settings.");return;}var store=new Storage();if(!demo)store.UpgradeLiveDefaults();var card=new Card(store,demo);app.MainWindow=card;card.Show();if(!demo&&!store.Settings.SetupCompleted)card.Dispatcher.BeginInvoke(new Action(card.ShowSetup));int render=Array.IndexOf(args,"--render-demo");if(render>=0&&render+1<args.Length){card.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{card.UpdateLayout();var bitmap=new RenderTargetBitmap((int)card.ActualWidth,(int)card.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(card);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(args[render+1]))encoder.Save(file);app.Shutdown();}));}app.Run();}
   }catch(Exception ex){MessageBox.Show("ProgressPeek could not start: "+ex.Message,"ProgressPeek");}
  }
  static void SyntheticCheck(string[] args){
   int index=Array.IndexOf(args,"--synthetic-check");string path=index+1<args.Length?args[index+1]:null;
   try{bool created;using(var exclusive=new Mutex(true,"Local\\TaskProgressOverlay",out created)){
    if(!created)throw new InvalidOperationException("Close the widget before a synthetic verification check.");
    var storage=new Storage();int count;string reason;if(!SetupConsent.AllowsAi(storage.Settings)||!storage.Reserve(Evidence.Hash("Synthetic verification"),"synthetic",true,DateTime.UtcNow,out count,out reason))throw new InvalidOperationException("Verification not enabled or check allowance unavailable");
    var result=new CliEstimator(storage).Run("Synthetic task only: Small task requires effort weight 10 and Large task effort weight 90. Return exactly these two steps, weighted 10 and 90. Small is completed; Large has not started.","Small task completed. Large task not started.",null,CancellationToken.None);var estimate=Estimate.Calculate(result.Map.Steps,"Small task completed. Large task not started.");
    if(path!=null)File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{success=true,input_tokens=result.InputTokens,output_tokens=result.OutputTokens,known=estimate.Known,low=estimate.Low,high=estimate.High,steps=result.Map.Steps.Count}));
   }}catch(Exception ex){if(path!=null)File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{success=false,error=CliEstimator.UserError(ex)}));Environment.ExitCode=1;}
  }
  static bool demoMutexCreated;
 }
}
