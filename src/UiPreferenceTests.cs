using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ProgressPeek;
public static class UiPreferenceTests {
 static int checks;static Action<bool,string> assertion;
 static void Check(bool value,string name){checks++;assertion(value,name);}
 [STAThread] public static void Main(){Run((value,name)=>{if(!value)throw new Exception(name);});Console.WriteLine("UI preferences: "+checks+" checks passed");}
 public static void Run(Action<bool,string> assert){assertion=assert;checks=0;
  Check(WidgetTheme.ClampScale(Double.NaN)==1,"Invalid text scale falls back to default");
  Check(WidgetTheme.ClampScale(.2)==.9&&WidgetTheme.ClampScale(2)==1.3,"Text scale is bounded");
  var w=new Window{FontSize=12};var panel=new StackPanel();w.Content=panel;var inherited=new TextBlock{Text="Inherited label"};panel.Children.Add(inherited);
  var text=new TextBlock{Text="Readable",FontSize=12};var dynamicText=new TextBlock{Text="45%",FontSize=34,Tag="dynamic-size"};panel.Children.Add(text);panel.Children.Add(dynamicText);
  var menu=new ContextMenu();menu.Items.Add(new MenuItem{Header="Settings"});w.ContextMenu=menu;
  var settings=new Settings{TextScale=1.3,Accent="Purple"};WidgetTheme.Install(w);WidgetTheme.Apply(w,settings);WidgetTheme.Apply(w,settings);
  Check(Math.Abs(text.FontSize-15.6)<.001,"Repeated Apply does not compound fonts");
  Check(Math.Abs(inherited.FontSize-15.6)<.001,"Inherited label font scales exactly once");
  Check(dynamicText.FontSize==34,"Dynamic percentage is not scaled twice");
  Check(menu.Resources["Accent"] is SolidColorBrush,"Popup has its own accent resource");
  var before=((SolidColorBrush)menu.Resources["Accent"]).Color;settings.Accent="Green";WidgetTheme.Apply(w,settings);
  Check(before!=((SolidColorBrush)menu.Resources["Accent"]).Color,"Existing popup updates its accent");
  settings.TextScale=1;WidgetTheme.Apply(w,settings);Check(text.FontSize==12,"Default scale restores baseline");
  Check(w.Resources[typeof(ScrollViewer)] is Style,"Scroll viewers use reserved-gutter theme");
  Check(w.Resources[typeof(ComboBox)] is Style&&menu.Resources[typeof(MenuItem)] is Style,"Controls and menus use explicit dark templates");
  var scroll=new ScrollViewer{Content=new TextBlock{Text=new String('x',600),TextWrapping=TextWrapping.Wrap},VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};w.Content=scroll;scroll.Measure(new Size(180,80));scroll.Arrange(new Rect(0,0,180,80));scroll.UpdateLayout();
  var scrollbar=scroll.Template.FindName("PART_VerticalScrollBar",scroll) as ScrollBar;var presenter=scroll.Template.FindName("PART_ScrollContentPresenter",scroll) as ScrollContentPresenter;
  Check(scrollbar!=null&&scrollbar.ActualWidth>=12&&scrollbar.Visibility==Visibility.Visible,"Scrollable content gets a reserved scrollbar gutter");
  Check(presenter!=null&&presenter.ActualWidth+scrollbar.ActualWidth<=scroll.ActualWidth+.01,"Scroll thumb cannot overlay content");
  scroll.ScrollToVerticalOffset(40);scroll.UpdateLayout();Check(scroll.VerticalOffset>0,"Custom scrollbar preserves native scroll offsets");
  w.Close();
 }
}
