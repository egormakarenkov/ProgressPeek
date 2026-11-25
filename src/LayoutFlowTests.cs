using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProgressPeek;
static class LayoutFlowTests {
 static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Field(object o,string name){return o.GetType().GetField(name,Private).GetValue(o);}
 static void Call(object o,string name){o.GetType().GetMethod(name,Private).Invoke(o,null);}
 static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
 static T Find<T>(DependencyObject node) where T:DependencyObject {var value=node as T;if(value!=null)return value;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=Find<T>(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
 public static void Run(Action<bool,string> check,string folder){
  var defaults=new Storage(Path.Combine(folder,"new-defaults"));check(defaults.Settings.Enabled,"new installations enable subscription estimates");defaults.Settings.Enabled=false;defaults.SaveSettings();check(!new Storage(defaults.Folder).Settings.Enabled,"explicit estimate opt-out survives restart");
  check(!new Settings().EnforceDailyLimit,"daily cap is off for new installations");defaults.Settings.EnforceDailyLimit=true;defaults.SaveSettings();check(new Storage(defaults.Folder).Settings.EnforceDailyLimit,"explicit daily cap opt-in survives restart");
  var fresh=new Card(new Storage(Path.Combine(folder,"fresh-layout")),true);fresh.Show();Pump();double initialHeight=fresh.ActualHeight;((TextBlock)Field(fresh,"percent")).Text="A much longer status that needs additional lines";Pump();check(fresh.SizeToContent==SizeToContent.Manual&&Math.Abs(fresh.ActualHeight-initialHeight)<1,"fresh unlocked cards do not resize on background labels");fresh.Close();
  var store=new Storage(Path.Combine(folder,"layout"));store.Settings.Enabled=false;store.Settings.WidgetWidth=450;store.Settings.WidgetHeight=260;var card=new Card(store,true);card.Show();Pump();
  try{
   double width=card.ActualWidth;Call(card,"ToggleDetails");Pump();double expanded=card.ActualHeight;
   check(expanded>300&&Math.Abs(card.ActualWidth-width)<1,"details grow only height after manual fixed-size setup");
   check(((ScrollViewer)Field(card,"bodyScroll")).ScrollableHeight<1,"expanded details fit without unnecessary outer scrollbar");
   Call(card,"ToggleDetails");Pump();check(card.ActualHeight<expanded&&Math.Abs(card.ActualWidth-width)<1,"hiding details shrinks vertically without changing width");
   store.Settings.LockSize=true;Call(card,"ApplyPreferences");Pump();double locked=card.ActualHeight;Call(card,"ToggleDetails");Pump();check(Math.Abs(card.ActualHeight-locked)<1,"size lock prevents automatic details resize");
   Call(card,"ToggleCollapse");Pump();Call(card,"ToggleCollapse");Pump();check(Math.Abs(card.ActualHeight-locked)<1,"locked card restores full height after compact toggle");
   store.Settings.LockSize=false;Call(card,"ApplyPreferences");Pump();double withExtras=card.ActualHeight;store.Settings.ShowUsage=store.Settings.ShowEta=store.Settings.ShowFreshness=false;Call(card,"ApplyPreferences");Pump();check(card.ActualHeight<withExtras&&Math.Abs(card.ActualWidth-width)<1,"saved layout preferences refit height without changing width");
   double beforeCollapsedChange=card.ActualHeight;Call(card,"ToggleCollapse");Pump();store.Settings.TextScale=1.3;Call(card,"ApplyPreferences");Call(card,"ToggleCollapse");Pump();check(card.ActualHeight>beforeCollapsedChange&&Math.Abs(card.ActualWidth-width)<1,"preferences saved while compact refit on expansion");
   var work=(Rect)card.GetType().GetMethod("WorkArea",Private).Invoke(card,null);card.Top=work.Bottom-20;((TextBlock)Field(card,"evidence")).Text=new string('x',20000);Call(card,"FitContentHeight");Pump();check(card.ActualHeight<=work.Height+1&&card.Top>=work.Top-1&&card.Top+card.ActualHeight<=work.Bottom+1&&Math.Abs(card.ActualWidth-width)<1,"long details stay inside screen while preserving width");check(((ScrollViewer)Field(card,"bodyScroll")).ScrollableHeight>0,"screen-limited details retain functional overflow scrolling");
   Call(card,"ShowSettings");Pump();var settings=(Window)Field(card,"settingsWindow");var tabs=Find<TabControl>(settings);var appearance=(StackPanel)((ScrollViewer)((TabItem)tabs.Items[0]).Content).Content;ComboBox combo=null;foreach(var item in appearance.Children){combo=item as ComboBox;if(combo!=null)break;}combo.IsDropDownOpen=true;Pump();check(settings.IsVisible,"opening settings dropdown does not dismiss settings");combo.IsDropDownOpen=false;
   combo.SelectedIndex=1;string savedAccent=store.Settings.Accent;var other=new Window{Width=80,Height=80,ShowInTaskbar=false};other.Show();other.Activate();Pump();check(Field(card,"settingsWindow")==null&&!settings.IsVisible,"settings dismiss when another window is activated");check(store.Settings.Accent==savedAccent,"click-away discards uncommitted settings changes");other.Close();
   Call(card,"ShowSettings");Pump();settings=(Window)Field(card,"settingsWindow");var owned=new Window{Owner=settings,Width=80,Height=80,ShowInTaskbar=false};owned.Show();owned.Activate();Pump();check(settings.IsVisible,"owned report windows keep settings open");owned.Close();settings.Activate();Pump();card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent});Pump();check(Field(card,"settingsWindow")==null,"click on nonactivating widget dismisses settings");
  }finally{var settings=Field(card,"settingsWindow") as Window;if(settings!=null)settings.Close();card.Close();}
 }
}
