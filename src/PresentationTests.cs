using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProgressPeek;
static class PresentationTests {
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(object o,string n){return o.GetType().GetField(n,Hidden).GetValue(o);}
 static void Call(object o,string n,params object[] args){o.GetType().GetMethod(n,Hidden).Invoke(o,args);}
 static T Find<T>(DependencyObject node) where T:DependencyObject {var value=node as T;if(value!=null)return value;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=Find<T>(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
 static void Save(FrameworkElement visual,string path){visual.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
 public static void Run(Action<bool,string> check,string folder,string renders=null){
  var store=new Storage(Path.Combine(folder,"presentation"));store.Settings.Enabled=false;store.Settings.WidgetWidth=300;store.Settings.TextScale=1.3;store.Settings.ShowUsage=false;var card=new Card(store,true);card.Show();card.UpdateLayout();
  try{
   Call(card,"Observe",new Snapshot{Chat="Fixture",Prompt="Fixture task",Generating=true});card.UpdateLayout();var text=(TextBlock)Field(card,"percent");double numeric=text.FontSize;
   Call(card,"Observe",new Snapshot{Error="Fixture accessibility gap"});card.UpdateLayout();
   check(text.Text=="Awaiting message"&&text.FontSize<numeric,"reading gap after percent uses smaller status typography");
   var formatted=new FormattedText(text.Text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,text.Foreground,1);
   check(formatted.WidthIncludingTrailingWhitespace<=text.ActualWidth+.5,"awaiting message fits one line at minimum width and maximum scale");
   check(((ScrollViewer)Field(card,"bodyScroll")).ScrollableHeight<1,"automatic content height fits scaled status without vertical clipping");
   if(renders!=null){Directory.CreateDirectory(renders);Save(card,Path.Combine(renders,"awaiting-min-width-max-scale.png"));}
   text.ToolTip="Experimental time remaining uses recent progress and observed active time. A longer explanation must wrap within the theme without additional AI calls.";
   text.RaiseEvent((ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{true},null));
   var tip=text.ToolTip as ToolTip;check(tip!=null,"string tooltip receives shared themed container at opening");
   if(tip!=null){tip.ApplyTemplate();tip.Measure(new Size(500,500));tip.Arrange(new Rect(tip.DesiredSize));tip.UpdateLayout();var wrapped=Find<TextBlock>(tip);var frame=Find<Border>(tip);
    check(tip.ActualWidth<=320&&tip.ActualHeight>45&&wrapped!=null&&wrapped.TextWrapping==TextWrapping.Wrap,"actual tooltip template wraps long strings within bounded width");
    check(frame!=null&&frame.CornerRadius.TopLeft>0&&tip.Padding.Left>=10&&((SolidColorBrush)tip.Background).Color.R<60,"tooltip has padded rounded dark theme");
    if(renders!=null)Save(tip,Path.Combine(renders,"dark-wrapped-tooltip.png"));
   }
   var opening=(ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{true},null);text.ToolTip="A newly refreshed ETA explanation";text.RaiseEvent(opening);check(text.ToolTip is ToolTip&&((ToolTip)text.ToolTip).Content.ToString().Contains("newly refreshed"),"dynamic tooltip replacements retain current content and theme");
   var refresh=(Button)Field(card,"refresh");refresh.RaiseEvent((ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{true},null));check(refresh.ToolTip is ToolTip,"widget controls share themed string tooltip behavior");
   var etaDetails=(TextBlock)Field(card,"etaDetails");check(etaDetails.FontSize>14,"ETA details inherit maximum text scale");
   var known=new Snapshot{Chat="Other",Prompt="Known fixture",Generating=true};Call(card,"Observe",known);card.GetType().GetField("map",Hidden).SetValue(card,new TaskMap{Low=40,High=50,Phase="Coding",Confidence="low",Explanation="Synthetic implementation evidence"});Call(card,"Observe",known);card.UpdateLayout();check(text.Text=="40–50%"&&text.FontSize>22*1.3,"percentage retains display hierarchy");check(((ScrollViewer)Field(card,"bodyScroll")).ScrollableHeight<1,"auto-fit percentage transition has no unnecessary outer scrollbar");if(renders!=null)Save(card,Path.Combine(renders,"known-percentage.png"));
  }finally{card.Close();}
  var policy=typeof(DesktopPolicy).GetMethod("ShouldShowOnMonitor");check(policy!=null,"monitor-aware visibility boundary exists");
  if(policy!=null){var anchor=new Rect(0,0,900,700);var first=new Rect(0,0,1920,1080);var second=new Rect(1920,0,1920,1080);Func<bool,bool,bool,bool,Rect,Rect,bool> show=(hidden,available,own,above,focus,monitor)=>(bool)policy.Invoke(null,new object[]{hidden,available,own,above,anchor,focus,first,monitor});
   check(!show(false,true,false,true,new Rect(100,100,800,600),first),"same monitor foreground takeover hides overlay");
   check(show(false,true,false,true,new Rect(2000,100,800,600),second),"other monitor foreground retains visible ChatGPT overlay");
   check(!show(false,true,false,true,new Rect(800,100,1400,600),second),"spanning foreground overlap hides overlay even with other monitor focus");
   check(show(false,true,true,true,new Rect(100,100,800,600),first),"own widget settings and menu cannot take over anchor");
   check(show(false,true,false,false,new Rect(100,100,800,600),first),"foreground below anchor does not occlude it");
   check(!show(false,false,true,false,Rect.Empty,first)&&!show(true,true,true,false,Rect.Empty,first),"closed minimized invisible cloaked and manually hidden anchors cannot expose text");
  }
  var now=new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc);var grace=new VisibilityGrace();check(grace.Show(true,true,now)&&grace.Show(true,false,now.AddMilliseconds(300))&&!grace.Show(true,false,now.AddMilliseconds(1100)),"brief foreground changes receive grace but persistent takeover hides");check(grace.Show(true,true,now.AddMilliseconds(1200))&&grace.Show(true,false,now.AddMilliseconds(1300)),"returning anchor visibility resets takeover grace");check(!grace.Show(false,true,now.AddMilliseconds(1400)),"unavailable or cloaked anchor hides immediately without grace");
  var native=new Window{Width=80,Height=80,ShowActivated=false,ShowInTaskbar=false};native.Show();var handle=new System.Windows.Interop.WindowInteropHelper(native).Handle;check(DesktopNative.Available(handle),"native DWM accepts synthetic visible uncloaked window");native.Hide();check(!DesktopNative.Available(handle),"native hidden window is unavailable");native.Show();native.WindowState=WindowState.Minimized;check(!DesktopNative.Available(handle),"native minimized window is unavailable");native.Close();check(!DesktopNative.Available(handle),"native closed window is unavailable");
 }
 [STAThread]static int Main(string[] args){int checks=0,failed=0;var folder=Path.Combine(Path.GetTempPath(),"ProgressPeek-presentation-"+Guid.NewGuid().ToString("N"));try{Run((ok,name)=>{checks++;if(!ok){failed++;Console.WriteLine("FAIL: "+name);}},folder,args.Length>0?args[0]:null);}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}Console.WriteLine("Presentation checks: "+checks+", failures: "+failed);return failed==0?0:1;}
}
