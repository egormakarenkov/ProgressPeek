using System;
using System.Windows.Forms;
namespace ProgressPeek {
 public sealed class TrayNotifications {
  readonly NotifyIcon tray;Action action;
  public TrayNotifications(NotifyIcon tray){this.tray=tray;tray.BalloonTipClicked+=(s,e)=>{var clicked=action;action=null;if(clicked!=null)clicked();};tray.BalloonTipClosed+=(s,e)=>action=null;}
  public void Show(int timeout,string title,string text,ToolTipIcon icon,Action clicked){action=clicked;try{tray.ShowBalloonTip(timeout,title,text,icon);}catch{action=null;throw;}}
 }
}
