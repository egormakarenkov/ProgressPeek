using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace ProgressPeek {
 public partial class Card {
  UpdateStaging updateStage;UpdateCandidate updateCandidate;CancellationTokenSource updateCancel;DispatcherTimer updateTimer;bool updateBusy,updateClosed;int updateEpoch;DateTime lastManualUpdate=DateTime.MinValue;string updateStatus="Not configured. A publisher-configured build is needed to enable updates.";TextBlock updatesStatus,updatesNotes;Button updateDownload,updateRestart;TabControl settingsTabs;const int UpdatesTabIndex=4;
  readonly TrayNotifications trayNotifications;
  void InitializeUpdates(){
   if(demo){updateStatus="Demo: update checks and installation are disabled.";return;}if(!UpdateSecurity.Configured)return;
   try{updateStage=new UpdateStaging(store.Folder);if(!String.IsNullOrEmpty(updateStage.State.Status))updateStatus=updateStage.State.Status;else updateStatus="Ready to check for updates.";updateTimer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};updateTimer.Tick+=(s,e)=>AutomaticUpdateCheck();updateTimer.Start();Loaded+=(s,e)=>AutomaticUpdateCheck();}catch{updateStatus="Update storage is unavailable. Use a manual download.";}
  }
  void StopUpdates(){updateClosed=true;updateEpoch++;if(updateCancel!=null)updateCancel.Cancel();if(updateTimer!=null)updateTimer.Stop();}
  void OpenUpdates(){ShowSettings();if(settingsTabs!=null)settingsTabs.SelectedIndex=UpdatesTabIndex;}
  void AutomaticUpdateCheck(){if(updateStage==null||!store.Settings.CheckUpdatesAutomatically||updateBusy)return;DateTime now=DateTime.UtcNow;if(updateStage.State.LastAttemptUtc>now){updateStage.State.LastAttemptUtc=now;try{updateStage.Save();}catch{}return;}if((now-updateStage.State.LastAttemptUtc).TotalHours>=6)CheckUpdates(false);}
  void UpdatePreferencesChanged(bool oldCheck,bool oldInstall,bool oldBeta){
   if(oldCheck==store.Settings.CheckUpdatesAutomatically&&oldInstall==store.Settings.InstallUpdatesAutomatically&&oldBeta==store.Settings.IncludePrereleases)return;updateEpoch++;if(updateCancel!=null)updateCancel.Cancel();updateBusy=false;updateCandidate=null;if(updateStage!=null){try{if(!store.Settings.InstallUpdatesAutomatically||!store.Settings.CheckUpdatesAutomatically||oldBeta!=store.Settings.IncludePrereleases)updateStage.Disarm();if(oldBeta!=store.Settings.IncludePrereleases&&!updateStage.RevalidateChannel(store.Settings.IncludePrereleases))updateStatus=updateStage.State.Status;}catch{updateStatus="Update preferences saved; automatic application could not be scheduled.";}}RenderUpdates();
  }
  void RenderUpdates(){if(updatesStatus!=null)updatesStatus.Text=updateStatus;if(updatesNotes!=null)updatesNotes.Text=updateCandidate==null?"":updateCandidate.Notes;if(updateDownload!=null)updateDownload.IsEnabled=!demo&&!updateBusy&&updateCandidate!=null;if(updateRestart!=null)updateRestart.IsEnabled=!demo&&!updateBusy&&updateStage!=null&&!String.IsNullOrEmpty(updateStage.State.PendingId);}
  void CheckUpdates(bool manual){
   if(demo||!UpdateSecurity.Configured||updateStage==null){RenderUpdates();return;}if(updateBusy)return;DateTime now=DateTime.UtcNow;if(manual&&(now-lastManualUpdate).TotalSeconds<10){updateStatus="Please wait a moment before checking again.";RenderUpdates();return;}if(manual)lastManualUpdate=now;
   updateStage.State.LastAttemptUtc=now;try{updateStage.Save();}catch{updateStatus="Cannot save update check timing. Use a manual download.";RenderUpdates();return;}
   updateBusy=true;updateStatus="Checking for updates…";RenderUpdates();int ticket=++updateEpoch;bool beta=store.Settings.IncludePrereleases;updateCancel=new CancellationTokenSource();var token=updateCancel.Token;
   Task.Factory.StartNew(()=>new UpdateClient().Discover(beta,token),token).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(()=>{
    if(updateClosed||ticket!=updateEpoch)return;updateBusy=false;if(task.IsCanceled||task.IsFaulted){updateStatus=token.IsCancellationRequested?"Update check cancelled.":"Could not verify an update. Try again later or use a manual download.";RenderUpdates();return;}
    updateCandidate=task.Result;updateStatus=updateCandidate==null?"No newer release is available for this channel.":"Update "+updateCandidate.Manifest.Version+" is available.";RenderUpdates();if(updateCandidate!=null){trayNotifications.Show(5000,"ProgressPeek update",updateStatus+" Open Updates for details.",System.Windows.Forms.ToolTipIcon.Info,()=>Dispatcher.BeginInvoke(new Action(OpenUpdates)));if(store.Settings.CheckUpdatesAutomatically&&store.Settings.InstallUpdatesAutomatically)DownloadUpdate(true);}
   })),TaskScheduler.Default);
  }
  void DownloadUpdate(bool automatic){
   if(demo||updateBusy||updateCandidate==null)return;updateBusy=true;updateStatus="Downloading and verifying update…";RenderUpdates();var candidate=updateCandidate;int ticket=++updateEpoch;updateCancel=new CancellationTokenSource();var token=updateCancel.Token;bool beta=store.Settings.IncludePrereleases;
   Task.Factory.StartNew(()=>new UpdateClient().Download(candidate,token),token).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(()=>{
    if(updateClosed||ticket!=updateEpoch)return;updateBusy=false;try{if(task.IsCanceled||task.IsFaulted||token.IsCancellationRequested)throw new IOException();updateStage.Stage(candidate,task.Result,Process.GetCurrentProcess().MainModule.FileName,beta,automatic&&store.Settings.CheckUpdatesAutomatically&&store.Settings.InstallUpdatesAutomatically);updateStatus=automatic?"Verified update ready. It will apply on your next normal launch.":"Verified update ready. Choose Restart now, or Later to keep working.";}catch{updateStatus="Update could not be staged. Your installed executable was kept. Try again or use a manual download.";}RenderUpdates();
   })),TaskScheduler.Default);
  }
  void RestartForUpdate(){if(demo||updateStage==null||updateBusy)return;try{if(updateStage.Launch(true,store.Settings))Application.Current.Shutdown();}catch{updateStatus="Update could not restart safely. Your installed executable was kept.";RenderUpdates();}}
  void PopulateUpdates(StackPanel panel){
   SectionHeading(panel,"Application updates");panel.Children.Add(SettingsText("Installed version: "+BetaDiagnostics.Version));panel.Children.Add(SettingsText("Updates use signed releases from the compiled publisher. A verified update keeps the previous executable and preserves your settings and check counts."));
   updatesStatus=SettingsText(updateStatus);panel.Children.Add(updatesStatus);var check=new Button{Content="Check now",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,10),IsEnabled=!demo&&UpdateSecurity.Configured};check.Click+=(s,e)=>CheckUpdates(true);panel.Children.Add(check);updatesNotes=SettingsText("");panel.Children.Add(updatesNotes);updateDownload=new Button{Content="Download update",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,10)};updateDownload.Click+=(s,e)=>DownloadUpdate(false);panel.Children.Add(updateDownload);var row=new StackPanel{Orientation=Orientation.Horizontal};updateRestart=new Button{Content="Restart now",Margin=new Thickness(0,0,10,0)};updateRestart.Click+=(s,e)=>RestartForUpdate();row.Children.Add(updateRestart);var later=new Button{Content="Later"};later.Click+=(s,e)=>{if(settingsWindow!=null)settingsWindow.Close();};row.Children.Add(later);panel.Children.Add(row);RenderUpdates();
  }
 }
}
