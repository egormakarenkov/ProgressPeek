using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ProgressPeek {
 public partial class Card {
  TextBlock compactPercent,etaText,etaDetails,freshnessText;MenuItem pauseItem;
  EtaTracker eta=new EtaTracker();string etaChat="";readonly EtaHistory etaHistory=new EtaHistory();readonly CompletionGate completionGate=new CompletionGate();
  bool finishedHidden,automaticallyCollapsed;DateTime finishDue=DateTime.MinValue;string finishKey="";
  void InitializeQualityOfLife(Grid header,TextBlock title){
   percent.Tag="dynamic-size";
   header.Children.Remove(title);var titleRow=new Grid();titleRow.ColumnDefinitions.Add(new ColumnDefinition());titleRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;titleRow.Children.Add(title);
   compactPercent=Text("",13,"#89ABFF");compactPercent.Tag="dynamic-size";compactPercent.FontWeight=FontWeights.SemiBold;compactPercent.VerticalAlignment=VerticalAlignment.Center;compactPercent.TextWrapping=TextWrapping.NoWrap;compactPercent.Margin=new Thickness(6,0,5,0);compactPercent.Visibility=Visibility.Collapsed;Grid.SetColumn(compactPercent,1);titleRow.Children.Add(compactPercent);header.Children.Insert(0,titleRow);
   System.Windows.Automation.AutomationProperties.SetName(compactPercent,"Compact progress");
   etaText=Text("Estimating time…",11,"#ADB3BF");etaText.Margin=new Thickness(0,8,0,0);etaText.ToolTip="Experimental: inferred from recent progress and active time, without extra AI calls. Not a countdown or deadline.";body.Children.Insert(body.Children.IndexOf(hint)+1,etaText);
   freshnessText=Text("No estimate yet",11,"#ADB3BF");freshnessText.Margin=new Thickness(0,5,0,0);body.Children.Insert(body.Children.IndexOf(etaText)+1,freshnessText);
   etaDetails=Text("",11,"#ADB3BF");etaDetails.Margin=new Thickness(0,8,0,0);details.Children.Insert(1,etaDetails);
   pauseItem=new MenuItem{Header="Pause checks"};pauseItem.Click+=(s,e)=>TogglePause();ContextMenu.Items.Insert(2,pauseItem);
   Add(ContextMenu,"Reset position",ResetPosition);
   ContextMenu.Opened+=(s,e)=>{WidgetTheme.Apply(this,store.Settings);if(!demo&&store.Settings.ShowUsage&&!usageBusy&&(DateTime.UtcNow-usageAttempt).TotalSeconds>=10)RequestUsage();};
   WidgetTheme.Install(this);ApplyPreferences();
  }
  void SetCollapsed(bool value,bool automatic){
   if(collapsed==value)return;collapsed=value;automaticallyCollapsed=value&&automatic;
   body.Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;bodyScroll.Visibility=body.Visibility;usageText.Visibility=store.Settings.ShowUsage&&!collapsed?Visibility.Visible:Visibility.Collapsed;
   if(collapsed){expandedHeight=SizeToContent==SizeToContent.Manual?Height:0;SizeToContent=SizeToContent.Height;}
   else if(expandedHeight>0){SizeToContent=SizeToContent.Manual;Height=expandedHeight;}
   refresh.Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;compactPercent.Visibility=collapsed?Visibility.Visible:Visibility.Collapsed;ApplySizeLock();UpdateQualityLabels(DateTime.UtcNow);if(!collapsed&&pendingContentFit){pendingContentFit=false;FitContentHeight();}
  }
  void TogglePause(){store.Settings.Paused=!store.Settings.Paused;store.SaveSettings();ApplyPreferences();}
  void ApplyPreferences(){
   if(!store.Settings.Enabled||store.Settings.Paused||store.Settings.ManualOnly){ResetCheck();checkStatus.Complete();}
   checkStatus.Limit="";WidgetTheme.Apply(this,store.Settings);ApplyClickThrough();ApplySizeLock();StartUsage();
   if(store.Settings.ReducedMotion){loading.Visibility=Visibility.Collapsed;sweep.Visibility=Visibility.Collapsed;((TranslateTransform)sweep.RenderTransform).BeginAnimation(TranslateTransform.XProperty,null);}
   if(latest!=null){bool prior=suppressAuto;suppressAuto=true;try{Observe(latest);}finally{suppressAuto=prior;}}
   UpdateQualityLabels(DateTime.UtcNow);
   if(collapsed)pendingContentFit=true;else FitContentHeight();
  }
  void QualityObservation(bool changed,bool reading,DateTime now){
   if(changed){string chat=current==null?"":Lifecycle.ChatKey(current);eta=etaHistory.Select(taskKey,lifecycle.Started&&chat==etaChat&&latest!=null&&String.IsNullOrEmpty(latest.Error),now);etaChat=chat;finishDue=DateTime.MinValue;finishKey="";finishedHidden=false;if(automaticallyCollapsed)SetCollapsed(false,false);}
   eta.Tick(now,!reading&&current!=null&&current.Generating&&lifecycle.Terminal.Length==0&&!store.Settings.Paused);
   if(!reading&&completionGate.Update(taskKey,current!=null&&current.Generating,lifecycle.Finished)){
    finishKey=taskKey;finishDue=now.AddSeconds(Math.Max(0,Math.Min(300,store.Settings.FinishDelaySeconds)));
    if(!demo){if(store.Settings.NotifyFinished)trayNotifications.Show(5000,"ProgressPeek","Response finished. This does not verify project success.",System.Windows.Forms.ToolTipIcon.Info,null);if(store.Settings.SoundFinished)System.Media.SystemSounds.Asterisk.Play();}
   }
   UpdateQualityLabels(now);
  }
  void UpdateQualityLabels(DateTime now){
   if(compactPercent==null)return;
   double scale=DesktopPolicy.Clamp(store.Settings.TextScale,.9,1.3);compactPercent.FontSize=13*scale;
   compactPercent.Text=percent.Text.Contains("%")?percent.Text:store.Settings.Paused?"Paused":current!=null&&current.Generating?"Working":"Waiting";
   compactPercent.Opacity=busy?.5:1;compactPercent.ToolTip=busy?"Updating estimate…":percent.Text;
   pauseItem.Header=store.Settings.Paused?"Resume checks":"Pause checks";
   freshnessText.Visibility=store.Settings.ShowFreshness?Visibility.Visible:Visibility.Collapsed;
   string age=lastEstimate==DateTime.MinValue?"No estimate yet":"Updated "+Math.Max(0,(int)(now-lastEstimate).TotalSeconds)+"s ago";
   if(lifecycle.Finished)freshnessText.Text="Completion detected locally";
   else if(store.Settings.Paused)freshnessText.Text="Checks paused · "+age;
   else if(!store.Settings.Enabled)freshnessText.Text="AI estimates disabled";
   else if(busy)freshnessText.Text="Updating estimate…";
   else if(latest!=null&&!String.IsNullOrEmpty(latest.Error))freshnessText.Text="Unable to read this chat · "+age;
   else if(!String.IsNullOrEmpty(checkStatus.Error))freshnessText.Text="Check failed · "+age;
   else if(checkStatus.Limit.Contains("limit"))freshnessText.Text=checkStatus.Limit+" · "+age;
   else freshnessText.Text=(store.Settings.ManualOnly?"Manual updates · ":"")+age+(String.IsNullOrEmpty(checkStatus.Limit)?"":" · "+checkStatus.Limit);
   freshnessText.ToolTip="Last scan: "+(lastScan==DateTime.MinValue?"none":lastScan.ToLocalTime().ToString("HH:mm:ss"))+"\nLast successful estimate: "+(lastEstimate==DateTime.MinValue?"none":lastEstimate.ToLocalTime().ToString("HH:mm:ss"))+"\n"+(busy?"Estimate in progress":latest!=null&&!String.IsNullOrEmpty(latest.Error)?latest.Error:checkStatus.Limit);
   etaText.Visibility=store.Settings.ShowEta?Visibility.Visible:Visibility.Collapsed;
   eta.Tick(now,current!=null&&current.Generating&&!lifecycle.Finished&&lifecycle.Terminal.Length==0&&!store.Settings.Paused&&(latest==null||String.IsNullOrEmpty(latest.Error)));
   etaText.Text=eta.Label(now,current!=null&&current.Generating,lifecycle.Finished,store.Settings.Paused||lifecycle.Terminal=="Awaiting your input",latest!=null&&!String.IsNullOrEmpty(latest.Error)||!String.IsNullOrEmpty(checkStatus.Error)||lifecycle.Terminal.Length>0&&lifecycle.Terminal!="Awaiting your input");
   etaText.ToolTip="Experimental beta · no extra AI calls.\n"+eta.Explanation;etaDetails.Text="Time remaining: "+eta.Explanation;etaDetails.Visibility=etaText.Visibility;
   refresh.IsEnabled=!busy&&!store.Settings.Paused;refresh.Opacity=refresh.IsEnabled?1:.45;RenderUsage();
  }
  void ApplyFinishedBehavior(DateTime now){
   if(finishDue==DateTime.MinValue||now<finishDue||!lifecycle.Finished||taskKey!=finishKey||movingWindow||resizingWindow||settingsHandle!=IntPtr.Zero||ContextMenu.IsOpen)return;
   finishDue=DateTime.MinValue;
   if(store.Settings.FinishBehavior=="Collapse")SetCollapsed(true,true);
   else if(store.Settings.FinishBehavior=="Hide"){finishedHidden=true;Hide();}
  }
  void ResetPosition(){
   finishedHidden=false;userHidden=false;store.Settings.OffsetX=-Math.Max(Width,300)-20;store.Settings.OffsetY=100;
   var work=WorkArea();Left=work.Right-Math.Min(Width,work.Width)-20;Top=work.Top+Math.Min(100,Math.Max(0,work.Height-ActualHeight));
   if(current!=null&&DesktopNative.IsWindow(current.Handle))SaveOffset();else store.SaveSettings();
   if(demo)Show();else Follow();
  }
  void SnapPosition(){
   if(!store.Settings.SnapEdges)return;var work=WorkArea();const double gap=12;
   if(Math.Abs(Left-work.Left)<=gap)Left=work.Left;else if(Math.Abs(Left+ActualWidth-work.Right)<=gap)Left=work.Right-ActualWidth;
   if(Math.Abs(Top-work.Top)<=gap)Top=work.Top;else if(Math.Abs(Top+ActualHeight-work.Bottom)<=gap)Top=work.Bottom-ActualHeight;
  }
 }
}
