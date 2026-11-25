using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ProgressPeek {
 public static class BugReportUi {
  public static Window Create(Window owner,Settings settings,string safeDiagnostics){return new BugReportWindow(owner,settings,safeDiagnostics);}
 }
 internal sealed class BugReportWindow:Window {
  internal readonly TextBox TitleInput,HappenedInput,ExpectedInput,StepsInput,Preview;
  internal readonly CheckBox IncludeDiagnostics;
  internal readonly Button Continue;
  internal readonly TextBlock Status;
  readonly string diagnostics;
  static SolidColorBrush Brush(string hex){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
  static TextBlock Label(string value,int size){return new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,7)};}
  static TextBox Input(StackPanel panel,string label,int maximum,bool multiline){
   panel.Children.Add(Label(label,12));var input=new TextBox{MaxLength=maximum,AcceptsReturn=multiline,MinHeight=multiline?66:30,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,0,0,7),Padding=new Thickness(7)};
   System.Windows.Automation.AutomationProperties.SetName(input,label);panel.Children.Add(input);return input;
  }
  internal BugReportWindow(Window owner,Settings settings,string safeDiagnostics){
   diagnostics=safeDiagnostics??"";Owner=owner;Title="ProgressPeek · Report a bug";ShowInTaskbar=false;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
   var area=SystemParameters.WorkArea;Width=Math.Min(820,Math.Max(1,area.Width-32));Height=Math.Min(760,Math.Max(1,area.Height-32));Left=area.Left+(area.Width-Width)/2;Top=area.Top+(area.Height-Height)/2;WindowStartupLocation=WindowStartupLocation.Manual;
   Background=Brush("#202126");Foreground=Brush("#E4E7EF");FontFamily=new FontFamily("Segoe UI");FontSize=13;
   var frame=new Border{BorderBrush=Brush("#505867"),BorderThickness=new Thickness(1),Padding=new Thickness(18),CornerRadius=new CornerRadius(10)};Content=frame;
   var grid=new Grid();frame.Child=grid;grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var top=new StackPanel();var header=new DockPanel{Background=Brushes.Transparent};var close=new Button{Content="Close",Padding=new Thickness(12,6,12,6)};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);close.Click+=(s,e)=>Close();header.Children.Add(Label("Report a bug",20));header.MouseLeftButtonDown+=(s,e)=>{if(e.Handled)return;try{DragMove();}catch(InvalidOperationException){}};top.Children.Add(header);
   top.Children.Add(Label("GitHub issues are public. Your written description is not automatically sanitized: remove chat text, names, credentials and personal paths before sharing. No screenshots are attached. Nothing is uploaded by this window.",12));grid.Children.Add(top);
   var content=new Grid();content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(content,1);grid.Children.Add(content);
   var form=new StackPanel{Margin=new Thickness(0,0,14,0)};content.Children.Add(new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
   TitleInput=Input(form,"Title (required to continue on GitHub)",BugReport.MaximumTitleLength,false);HappenedInput=Input(form,"What happened",BugReport.MaximumFieldLength,true);ExpectedInput=Input(form,"What I expected",BugReport.MaximumFieldLength,true);StepsInput=Input(form,"Steps to reproduce",BugReport.MaximumFieldLength,true);
   IncludeDiagnostics=new CheckBox{Content="Include safe diagnostics in preview",IsChecked=false,Margin=new Thickness(0,10,0,8)};form.Children.Add(IncludeDiagnostics);form.Children.Add(Label("Optional diagnostics contain only allowlisted settings and scan metadata. Review the exact report alongside this form.",12));
   var right=new Grid();right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition());right.Children.Add(Label("Exact report preview",14));Preview=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(10),FontFamily=new FontFamily("Consolas"),FontSize=12};System.Windows.Automation.AutomationProperties.SetName(Preview,"Exact bug report preview");Grid.SetRow(Preview,1);right.Children.Add(Preview);Grid.SetColumn(right,1);content.Children.Add(right);
   var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var buttons=new WrapPanel();var copy=new Button{Content="Copy",Padding=new Thickness(14,6,14,6),Margin=new Thickness(0,0,8,6)};var save=new Button{Content="Save…",Padding=new Thickness(14,6,14,6),Margin=new Thickness(0,0,8,6)};Continue=new Button{Content="Continue on GitHub",Padding=new Thickness(14,6,14,6),Margin=new Thickness(0,0,0,6)};buttons.Children.Add(copy);buttons.Children.Add(save);buttons.Children.Add(Continue);footer.Children.Add(buttons);Status=Label("",12);footer.Children.Add(Status);Grid.SetRow(footer,2);grid.Children.Add(footer);
   TitleInput.TextChanged+=(s,e)=>RefreshPreview();HappenedInput.TextChanged+=(s,e)=>RefreshPreview();ExpectedInput.TextChanged+=(s,e)=>RefreshPreview();StepsInput.TextChanged+=(s,e)=>RefreshPreview();IncludeDiagnostics.Checked+=(s,e)=>RefreshPreview();IncludeDiagnostics.Unchecked+=(s,e)=>RefreshPreview();
   copy.Click+=(s,e)=>{if(CopyPreview())Status.Text="Copied the previewed report. Review before sharing.";};
   save.Click+=(s,e)=>{
    try{var dialog=new SaveFileDialog{Title="Save bug report",FileName="ProgressPeek-bug-report.md",Filter="Markdown report (*.md)|*.md|Text file (*.txt)|*.txt",DefaultExt=".md",AddExtension=true,OverwritePrompt=true};if(dialog.ShowDialog(this)!=true)return;File.WriteAllText(dialog.FileName,Preview.Text,new UTF8Encoding(false,true));Status.Text="Saved the previewed report to your selected file.";}catch{Status.Text="Could not save the report. Choose a writable destination and try again.";}
   };
   Continue.Click+=(s,e)=>{
    string url=BugReport.BuildIssueUrl(DistributionConfig.Repository,TitleInput.Text);if(url.Length==0){RefreshPreview();return;}
    if(!CopyPreview())return;
    try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});Status.Text="Report copied. Paste it into the issue form, review everything, then submit on GitHub.";}catch{Status.Text="Report copied, but the browser could not open. Open the configured repository's issue page and paste, review, then submit.";}
   };
   PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};RefreshPreview();WidgetTheme.Apply(this,settings??new Settings());
  }
  bool CopyPreview(){try{Clipboard.SetText(Preview.Text);return true;}catch{Status.Text="Clipboard unavailable. Select and copy the preview manually.";return false;}}
  void RefreshPreview(){
   Preview.Text=BugReport.Format(TitleInput.Text,HappenedInput.Text,ExpectedInput.Text,StepsInput.Text,IncludeDiagnostics.IsChecked==true,diagnostics);
   Continue.IsEnabled=DistributionConfig.IsRepositoryConfigured&&!String.IsNullOrWhiteSpace(TitleInput.Text);
   Status.Text=DistributionConfig.IsRepositoryConfigured?"Continue copies the report and opens an issue form. Paste, review and submit there.":"Not configured — GitHub reporting is unavailable. You can still copy or save this report locally.";
  }
 }
 public partial class Card {
  void ShowBugReport(Window owner,SetupCheckResult setup=null){
   // Capture safe diagnostics once; opening this dialog performs no check and makes no model call.
   string safe=BetaDiagnostics.Report(store.Settings,demo?null:latest??current,checkStatus.Error,setup);
   BugReportUi.Create(owner??this,store.Settings,safe).ShowDialog();
  }
 }
}
