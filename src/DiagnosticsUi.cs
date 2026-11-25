using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProgressPeek {
 public partial class Card {
  void PopulateTroubleshooting(StackPanel panel){
   var appStatus=Text("",12,"#D8DBE5");var chatStatus=Text("",12,"#D8DBE5");var cliStatus=Text("CLI found: not checked",12,"#D8DBE5");var loginStatus=Text("Login status: not checked",12,"#D8DBE5");
   var guidance=Text("Check setup reads local CLI sign-in status without asking a model. It does not verify quota, connectivity, or estimate readiness.",12,"#AEB4C3");
   guidance.Margin=new Thickness(0,8,0,8);panel.Children.Add(appStatus);panel.Children.Add(chatStatus);panel.Children.Add(cliStatus);panel.Children.Add(loginStatus);panel.Children.Add(guidance);
   Action refreshScan=()=>{var snapshot=latest??current;appStatus.Text=demo?"App detected: demo (synthetic)":"App detected in last scan: "+(snapshot!=null&&snapshot.Handle!=IntPtr.Zero?"yes":"no — open Codex");chatStatus.Text=demo?"Chat readable: demo (synthetic)":"Chat readable in last scan: "+(BetaDiagnostics.Readable(snapshot)?"yes":"no — open a chat with a submitted prompt");};refreshScan();
   var buttons=new WrapPanel{Margin=new Thickness(0,3,0,8)};var check=Control("Check setup","Check setup without a model call");var preview=Control("Preview report","Preview safe diagnostic report");preview.Margin=new Thickness(8,0,0,0);var bug=Control("Report a bug","Create a local bug report");bug.Margin=new Thickness(8,0,0,0);buttons.Children.Add(check);buttons.Children.Add(preview);buttons.Children.Add(bug);panel.Children.Add(buttons);
   panel.Children.Add(Text("If Codex is detected but the chat is unreadable, bring the chat into view and reopen Settings. If the CLI is missing, install or update Codex, then check again. If plan sign-in is not confirmed, sign in using ChatGPT in Codex. This check never changes your login.",12,"#AEB4C3"));
   panel.Children.Add(Text("The report contains allowlisted settings and scan metadata only. Preview it before copying. Nothing is uploaded.",12,"#AEB4C3"));
   CancellationTokenSource pending=null;SetupCheckResult setup=demo?new SetupCheckResult{State=SetupCheckState.Demo}:null;bool alive=true;int generation=0;Window owner=null;
   bug.Click+=(s,e)=>{if(alive)ShowBugReport(Window.GetWindow(panel),setup);};
   // The worker owns disposal, so tab unload cannot dispose a token source while it registers cancellation.
   Action stop=()=>{alive=false;generation++;if(pending!=null){pending.Cancel();pending=null;loginStatus.Text="Login status: check cancelled";}};
   EventHandler onClosed=(s,e)=>stop();
   panel.Loaded+=(s,e)=>{alive=true;var next=Window.GetWindow(panel);if(owner!=next){if(owner!=null)owner.Closed-=onClosed;owner=next;if(owner!=null)owner.Closed+=onClosed;}refreshScan();};
   panel.Unloaded+=(s,e)=>{stop();if(owner!=null)owner.Closed-=onClosed;owner=null;check.IsEnabled=true;};
   check.Click+=async(s,e)=>{
    if(!alive||pending!=null)return;refreshScan();check.IsEnabled=false;loginStatus.Text="Login status: checking…";int ticket=++generation;var source=new CancellationTokenSource();var token=source.Token;pending=source;
    try {
     var result=await Task.Run(()=>SetupChecker.Check(store,demo,token));
     if(!alive||ticket!=generation||source.IsCancellationRequested)return;setup=result;
     cliStatus.Text=result.State==SetupCheckState.Demo?"CLI found: demo (not checked)":"CLI found: "+(result.CliFound?"yes":"no");
     switch(result.State){
      case SetupCheckState.Demo:loginStatus.Text="Login status: demo (synthetic)";break;
      case SetupCheckState.PlanLogin:loginStatus.Text="Login status: ChatGPT plan sign-in detected";break;
      case SetupCheckState.MissingCli:loginStatus.Text="Login status: unavailable — CLI not found";break;
      case SetupCheckState.OtherLogin:loginStatus.Text="Login status: ChatGPT plan sign-in not confirmed";break;
      case SetupCheckState.TimedOut:loginStatus.Text="Login status: check timed out — retry when Codex responds";break;
      default:loginStatus.Text="Login status: check failed — update Codex and retry";break;
     }
    }catch(OperationCanceledException){}catch{if(alive&&ticket==generation)loginStatus.Text="Login status: check could not complete";}
    finally{if(alive&&ticket==generation){check.IsEnabled=true;pending=null;}source.Dispose();}
   };
   preview.Click+=(s,e)=>{
    if(!alive)return;
    // Capture once: clipboard contents always match the exact report being previewed.
    string report=BetaDiagnostics.Report(store.Settings,demo?null:latest??current,checkStatus.Error,setup);
    var area=SystemParameters.WorkArea;double width=Math.Min(600,Math.Max(1,area.Width-32)),height=Math.Min(620,Math.Max(1,area.Height-32));
    var window=new Window{Title="ProgressPeek · diagnostic report",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=width,Height=height,Left=area.Left+(area.Width-width)/2,Top=area.Top+(area.Height-height)/2,Background=Color("#202126"),Foreground=Color("#E4E7EF"),FontFamily=new FontFamily("Segoe UI"),WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false};
    var parent=Window.GetWindow(panel);if(parent!=null)window.Owner=parent;
    var frame=new Border{BorderBrush=Color("#505867"),BorderThickness=new Thickness(1),Padding=new Thickness(18),CornerRadius=new CornerRadius(10)};window.Content=frame;
    var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});frame.Child=layout;
    var top=new StackPanel();var header=new DockPanel{Background=Brushes.Transparent,Margin=new Thickness(0,0,0,12)};var headerClose=new Button{Content="Close",Padding=new Thickness(12,6,12,6),Margin=new Thickness(10,0,0,0)};DockPanel.SetDock(headerClose,Dock.Right);header.Children.Add(headerClose);headerClose.Click+=(sender,args)=>window.Close();
    var title=Text("Diagnostic report",18,"#AFC8FF");title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(title);header.MouseLeftButtonDown+=(sender,args)=>{if(args.Handled)return;try{window.DragMove();}catch(InvalidOperationException){}};top.Children.Add(header);
    var intro=Text("Review before copying. This report stays on this device until you choose to share it.",13,"#C9CFDA");intro.Margin=new Thickness(0,0,0,12);top.Children.Add(intro);layout.Children.Add(top);
    var content=new TextBox{Text=report,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Color("#17191F"),Foreground=Color("#D8DBE5"),BorderBrush=Color("#424753"),Padding=new Thickness(10),FontFamily=new FontFamily("Consolas"),FontSize=12};Grid.SetRow(content,1);layout.Children.Add(content);
    var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var copy=new Button{Content="Copy report",HorizontalAlignment=HorizontalAlignment.Left};System.Windows.Automation.AutomationProperties.SetName(copy,"Copy the previewed diagnostic report");var notice=Text("",12,"#AEB4C3");notice.Margin=new Thickness(0,5,0,0);footer.Children.Add(copy);footer.Children.Add(notice);Grid.SetRow(footer,2);layout.Children.Add(footer);
    copy.Click+=(sender,args)=>{try{Clipboard.SetText(report);notice.Text="Copied";}catch{notice.Text="Clipboard unavailable; select and copy the text.";}};window.PreviewKeyDown+=(sender,args)=>{if(args.Key==System.Windows.Input.Key.Escape){window.Close();args.Handled=true;}};WidgetTheme.Apply(window,store.Settings);window.ShowDialog();
   };
  }
 }
}
