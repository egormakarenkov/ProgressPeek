using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace ProgressPeek {
 public static class DesktopPolicy {
  public static bool ShouldShow(bool hidden,bool exists,bool visible,bool minimized){return !hidden&&exists&&visible&&!minimized;}
  // A foreground application takes over the anchor monitor when it is above the
  // anchor there, or spans across the anchor from a neighboring monitor.
  public static bool ShouldShowOnMonitor(bool hidden,bool anchorAvailable,bool ownForeground,bool foregroundAbove,Rect anchor,Rect foreground,Rect anchorMonitor,Rect foregroundMonitor){
   if(hidden||!anchorAvailable)return false;if(ownForeground||!foregroundAbove)return true;
   return anchorMonitor!=foregroundMonitor&&!anchor.IntersectsWith(foreground);
  }
  public static double Clamp(double value,double low,double high){if(Double.IsNaN(value)||Double.IsInfinity(value))return low;return Math.Max(low,Math.Min(Math.Max(low,high),value));}
  public static Size FitSize(double width,double height,Size available){return new Size(Clamp(width,Math.Min(300,available.Width),Math.Min(800,available.Width)),Clamp(height,Math.Min(240,available.Height),Math.Min(1000,available.Height)));}
  public static bool ParseHotkey(string chord,out uint modifiers,out uint key){modifiers=key=0;if(String.IsNullOrWhiteSpace(chord))return false;var pieces=chord.Split('+');if(pieces.Length<2)return false;for(int i=0;i<pieces.Length-1;i++){uint flag;switch(pieces[i].Trim().ToLowerInvariant()){case "ctrl":case "control":flag=2;break;case "alt":flag=1;break;case "shift":flag=4;break;case "win":flag=8;break;default:return false;}if((modifiers&flag)!=0)return false;modifiers|=flag;}string last=pieces[pieces.Length-1].Trim().ToUpperInvariant();int f;if(last.Length==1&&((last[0]>='A'&&last[0]<='Z')||(last[0]>='0'&&last[0]<='9')))key=last[0];else if(last.StartsWith("F")&&Int32.TryParse(last.Substring(1),out f)&&f>=1&&f<=24)key=(uint)(0x6F+f);return key>0&&(modifiers&11)!=0;}
 }
 internal static class DesktopNative {
  [DllImport("user32.dll")]internal static extern bool IsWindow(IntPtr handle);
  [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr handle);
  [DllImport("user32.dll")]internal static extern IntPtr GetWindow(IntPtr handle,uint command);
  [DllImport("user32.dll")]internal static extern bool IsChild(IntPtr parent,IntPtr child);
  [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterHotKey(IntPtr handle,int id,uint modifiers,uint key);
  [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(IntPtr handle,int id);
  [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr handle,int attribute,out int value,int size);
  internal static bool Available(IntPtr handle){if(!IsWindow(handle)||!IsWindowVisible(handle)||Native.IsIconic(handle))return false;int cloaked;try{return DwmGetWindowAttribute(handle,14,out cloaked,4)==0&&cloaked==0;}catch(DllNotFoundException){return false;}catch(EntryPointNotFoundException){return false;}}
  static Rect Bounds(IntPtr handle){Native.RECT r;return Native.GetWindowRect(handle,out r)&&r.Right>r.Left&&r.Bottom>r.Top?new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top):Rect.Empty;}
  static Rect MonitorBounds(IntPtr handle){var r=System.Windows.Forms.Screen.FromHandle(handle).Bounds;return new Rect(r.Left,r.Top,r.Width,r.Height);}
  internal static bool Eligible(IntPtr anchor){
   var foreground=Native.GetForegroundWindow();uint pid;Native.GetWindowThreadProcessId(foreground,out pid);bool own=pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id||foreground==anchor;
   if(own||foreground==IntPtr.Zero||!Available(foreground))return true;
   bool above=false;IntPtr cursor=foreground;for(int i=0;i<2048&&cursor!=IntPtr.Zero;i++,cursor=GetWindow(cursor,2)){if(cursor==anchor){above=true;break;}}
   var anchorBounds=Bounds(anchor);if(anchorBounds.IsEmpty)return false;
   return DesktopPolicy.ShouldShowOnMonitor(false,true,own,above,anchorBounds,Bounds(foreground),MonitorBounds(anchor),MonitorBounds(foreground));
  }
 }
 public sealed class VisibilityGrace {
  DateTime takeoverSince=DateTime.MinValue;
  public bool Show(bool anchorAvailable,bool eligible,DateTime now){if(!anchorAvailable){takeoverSince=DateTime.MinValue;return false;}if(eligible){takeoverSince=DateTime.MinValue;return true;}if(takeoverSince==DateTime.MinValue)takeoverSince=now;return (now-takeoverSince).TotalMilliseconds<750;}
 }
 public sealed class HotkeyBinding:IDisposable {
  readonly HwndSource source;readonly Action toggle;int id;uint mods,key;
  public HotkeyBinding(HwndSource source,Action toggle){this.source=source;this.toggle=toggle;source.AddHook(Hook);}
  public bool Set(bool enabled,string chord,out string error){error="";if(!enabled){if(id!=0)DesktopNative.UnregisterHotKey(source.Handle,id);id=0;return true;}uint m,k;if(!DesktopPolicy.ParseHotkey(chord,out m,out k)){error="Use Ctrl, Alt or Win with a letter, number or F-key (for example Ctrl+Alt+P).";return false;}if(id!=0&&mods==m&&key==k)return true;int next=id==0x621?0x622:0x621;if(!DesktopNative.RegisterHotKey(source.Handle,next,m|0x4000,k)){error="That shortcut is in use. Choose another combination.";return false;}if(id!=0)DesktopNative.UnregisterHotKey(source.Handle,id);id=next;mods=m;key=k;return true;}
  IntPtr Hook(IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled){if(message==0x0312&&w.ToInt32()==id&&id!=0){handled=true;toggle();}return IntPtr.Zero;}
  public void Dispose(){if(id!=0)DesktopNative.UnregisterHotKey(source.Handle,id);source.RemoveHook(Hook);id=0;}
 }
 public partial class Card {
  Thumb resizeGrip;ScrollViewer bodyScroll;TextBlock usageText;HotkeyBinding hotkey;bool userHidden,closed;int displayHigh;double expandedHeight,sweepTravel=-1;
  FrameworkElement dragHeader;bool movingWindow,resizingWindow,contentFitted,pendingContentFit;
  readonly VisibilityGrace visibilityGrace=new VisibilityGrace();
  void InitializeDragging(Border shell,FrameworkElement header){
   dragHeader=header;
   shell.PreviewMouseLeftButtonDown+=(s,e)=>{
    if(!IsDragSurface(e.GetPosition(this)))return;
    e.Handled=true;
    if(e.ClickCount==2){ToggleCollapse();return;}
    if(store.Settings.LockPosition)return;
    // DragMove pumps dispatcher events: the follow timer must not restore old offsets.
    movingWindow=true;
    try{DragMove();}catch(InvalidOperationException){}
    finally{try{SnapPosition();SaveOffset();}finally{movingWindow=false;}}
   };
  }
  bool IsDragSurface(Point point){
   if(dragHeader==null||point.X<0||point.X>ActualWidth||point.Y<0||point.Y>dragHeader.TranslatePoint(new Point(0,dragHeader.ActualHeight),this).Y)return false;
   var hit=VisualTreeHelper.HitTest(this,point);var source=hit==null?null:hit.VisualHit;
   while(source!=null&&source!=this){if(source is ButtonBase)return false;source=VisualTreeHelper.GetParent(source);}
   return hit!=null;
  }
  void InitializeDesktop(Border shell,StackPanel root){
   MinWidth=300;MaxWidth=800;Width=DesktopPolicy.Clamp(store.Settings.WidgetWidth,300,800);
   if(store.Settings.WidgetHeight>0){Height=DesktopPolicy.Clamp(store.Settings.WidgetHeight,240,1000);SizeToContent=SizeToContent.Manual;}
   root.Children.Remove(body);shell.Child=null;var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.Children.Add(root);
   bodyScroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(bodyScroll,1);layout.Children.Add(bodyScroll);
   usageText=Text(demo?"Weekly · 93% left (demo)":"Reading account usage…",11,"#ADB3BF");usageText.Margin=new Thickness(0,12,0,0);usageText.Visibility=store.Settings.ShowUsage?Visibility.Visible:Visibility.Collapsed;Grid.SetRow(usageText,2);layout.Children.Add(usageText);shell.Child=layout;
   var outer=new Grid();Content=outer;outer.Children.Add(shell);resizeGrip=new Thumb{Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,5,5),Cursor=Cursors.SizeNWSE,ToolTip="Drag to resize"};System.Windows.Automation.AutomationProperties.SetName(resizeGrip,"Resize widget");var glyph=new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));glyph.SetValue(System.Windows.Shapes.Path.DataProperty,Geometry.Parse("M 5,15 L 15,5 M 10,15 L 15,10"));glyph.SetValue(System.Windows.Shapes.Path.StrokeProperty,Color("#757D8C"));glyph.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty,1.5);glyph.SetValue(System.Windows.Shapes.Path.MarginProperty,new Thickness(2));resizeGrip.Template=new ControlTemplate(typeof(Thumb)){VisualTree=glyph};outer.Children.Add(resizeGrip);
   resizeGrip.DragStarted+=(s,e)=>resizingWindow=true;
   resizeGrip.DragDelta+=(s,e)=>ResizeCard(e.HorizontalChange,e.VerticalChange);resizeGrip.DragCompleted+=(s,e)=>{try{store.Settings.WidgetWidth=DesktopPolicy.Clamp(ActualWidth>0?ActualWidth:Width,300,800);store.Settings.WidgetHeight=!contentFitted&&SizeToContent==SizeToContent.Manual?DesktopPolicy.Clamp(ActualHeight>0?ActualHeight:Height,240,1000):0;store.SaveSettings();SaveOffset();}finally{resizingWindow=false;}};ApplySizeLock();SizeChanged+=(s,e)=>UpdateBarWidth();
   SourceInitialized+=(s,e)=>{if(demo)return;hotkey=new HotkeyBinding(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle),ToggleWidget);string error;if(!hotkey.Set(store.Settings.HotkeyEnabled,store.Settings.Hotkey,out error))trayNotifications.Show(5000,"ProgressPeek shortcut",error,System.Windows.Forms.ToolTipIcon.Warning,null);StartUsage();};
   Closed+=(s,e)=>{closed=true;if(hotkey!=null)hotkey.Dispose();StopUsage();};
  }
  void ApplySizeLock(){resizeGrip.Visibility=store.Settings.LockSize||collapsed?Visibility.Collapsed:Visibility.Visible;resizeGrip.IsEnabled=!store.Settings.LockSize&&!collapsed;if(store.Settings.LockSize&&!collapsed&&IsLoaded&&ActualHeight>0&&SizeToContent!=SizeToContent.Manual){Height=ActualHeight;SizeToContent=SizeToContent.Manual;}}
  void FitContentHeight(){
   if(!IsLoaded||closed||collapsed||store.Settings.LockSize||movingWindow||resizingWindow)return;
   var content=Content as FrameworkElement;if(content==null)return;var work=WorkArea();double width=ActualWidth;
   content.InvalidateMeasure();content.Measure(new Size(width,Double.PositiveInfinity));double height=Math.Min(work.Height,Math.Ceiling(content.DesiredSize.Height));
   if(height<=0||Double.IsNaN(height)||Double.IsInfinity(height))return;
   contentFitted=true;MaxHeight=work.Height;SizeToContent=SizeToContent.Manual;Height=height;UpdateLayout();
   double top=DesktopPolicy.Clamp(Top,work.Top,work.Bottom-ActualHeight);if(Math.Abs(Top-top)>.5){Top=top;SaveOffset();}
  }
  Rect WorkArea(){var target=current!=null&&DesktopNative.IsWindow(current.Handle)?current.Handle:new WindowInteropHelper(this).Handle;var screen=System.Windows.Forms.Screen.FromHandle(target).WorkingArea;var source=PresentationSource.FromVisual(this);var matrix=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;var lo=matrix.Transform(new Point(screen.Left,screen.Top));var hi=matrix.Transform(new Point(screen.Right,screen.Bottom));return new Rect(lo,hi);}
  void ResizeCard(double dx,double dy){if(store.Settings.LockSize||collapsed)return;contentFitted=false;var size=DesktopPolicy.FitSize(ActualWidth+dx,ActualHeight+dy,WorkArea().Size);SizeToContent=SizeToContent.Manual;Width=size.Width;Height=size.Height;UpdateBarWidth();}
  void UpdateBarWidth(){var track=bar.Parent as FrameworkElement;double width=track!=null&&track.ActualWidth>0?track.ActualWidth:Math.Max(0,Width-40);bar.Width=width*displayHigh/100.0;if(busy&&sweep.Visibility==Visibility.Visible)AnimateSweep();}
  void AnimateSweep(bool force=false){var track=bar.Parent as FrameworkElement;double width=track!=null&&track.ActualWidth>0?track.ActualWidth:Math.Max(0,Width-40);double travel=Math.Max(0,width-sweep.Width);if(!force&&Math.Abs(travel-sweepTravel)<1)return;sweepTravel=travel;((TranslateTransform)sweep.RenderTransform).BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(0,travel,TimeSpan.FromSeconds(1.3)){AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});}
  void ToggleWidget(){if(finishedHidden){finishedHidden=false;userHidden=false;finishDue=DateTime.MinValue;if(demo)Show();else Follow();return;}userHidden=!userHidden;if(userHidden){ResetCheck();checkStatus.Complete();Hide();}else{if(latest!=null)Observe(latest);if(demo)Show();else Follow();}}
  void Follow(){
   bool available=current!=null&&DesktopNative.Available(current.Handle);
   if(userHidden||finishedHidden||!available){visibilityGrace.Show(false,false,DateTime.UtcNow);if(IsVisible)Hide();return;}
   if(movingWindow||resizingWindow)return;
   if(!visibilityGrace.Show(true,DesktopNative.Eligible(current.Handle),DateTime.UtcNow)){if(IsVisible)Hide();return;}
   Native.RECT rect;if(!Native.GetWindowRect(current.Handle,out rect))return;var source=PresentationSource.FromVisual(this);var matrix=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;var corner=matrix.Transform(new Point(rect.Right,rect.Top));var work=WorkArea();
   MinWidth=Math.Min(300,work.Width);MaxWidth=Math.Min(800,work.Width);MaxHeight=work.Height;var size=DesktopPolicy.FitSize(Width,ActualHeight,work.Size);if(Width>size.Width)Width=size.Width;double maxHeight=contentFitted?work.Height:size.Height;if(SizeToContent==SizeToContent.Manual&&Height>maxHeight)Height=maxHeight;
   double left=DesktopPolicy.Clamp(corner.X+store.Settings.OffsetX,work.Left,work.Right-ActualWidth),top=DesktopPolicy.Clamp(corner.Y+store.Settings.OffsetY,work.Top,work.Bottom-ActualHeight);if(Math.Abs(Left-left)>1)Left=left;if(Math.Abs(Top-top)>1)Top=top;if(!IsVisible)Show();
  }
 }
}
