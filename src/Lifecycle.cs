using System;
namespace ProgressPeek {
 // Response lifecycle is independent of the model's estimated remaining effort.
 public class Lifecycle {
  public Snapshot LastValid;public bool Running,Finished,Started;public string Terminal="",Key="";
  DateTime quietSince=DateTime.MinValue;string quietOutput="";
  public static string ChatKey(Snapshot s){return s.Handle+"\n"+s.PanelIdentity+"\n"+(String.IsNullOrWhiteSpace(s.Chat)?s.ChatIdentity:s.Chat);}
  public static string TaskKey(Snapshot s){return Evidence.Hash(ChatKey(s)+"\n"+s.Prompt+"\n"+s.TaskIdentity);}
  public bool Update(Snapshot s,DateTime now){
   if(!String.IsNullOrEmpty(s.Error)||String.IsNullOrWhiteSpace(s.Prompt)){
    // Unknown reading is not an ended response. Keep the window anchor across renderer churn.
    bool different=LastValid!=null&&(!String.IsNullOrEmpty(s.Chat)&&s.Chat!=LastValid.Chat||!String.IsNullOrEmpty(s.PanelIdentity)&&s.PanelIdentity!=LastValid.PanelIdentity);
    if(different){LastValid=s;Key="";Running=Finished=Started=false;Terminal="";quietSince=DateTime.MinValue;return true;}
    if(LastValid==null||s.Handle!=IntPtr.Zero&&LastValid.Handle!=s.Handle){LastValid=s;return false;}
    return false;
   }
   string key=TaskKey(s);bool changed=key!=Key||(s.Generating&&(Finished||Terminal.Length>0&&Terminal!="Awaiting your input"));
   if(changed){Started=String.IsNullOrWhiteSpace(s.Output)&&!s.FinalVisible&&String.IsNullOrEmpty(s.Terminal)||s.Generating&&LastValid!=null&&ChatKey(LastValid)==ChatKey(s)&&(key!=Key||Finished||Terminal.Length>0);Key=key;Running=Finished=false;Terminal="";quietSince=DateTime.MinValue;quietOutput="";}
   if(s.Generating&&Terminal=="Awaiting your input")Terminal="";
   LastValid=s;
   if(s.Generating){Running=true;quietSince=DateTime.MinValue;}
   else if(!String.IsNullOrEmpty(s.Terminal)){Terminal=s.Terminal;Running=Finished=false;quietSince=DateTime.MinValue;}
   else if(Terminal.Length==0&&!Finished&&s.ResponseReadComplete&&s.FinalVisible&&!String.IsNullOrWhiteSpace(s.Output)){
    if(quietSince==DateTime.MinValue||quietOutput!=s.Output){quietSince=now;quietOutput=s.Output;}
    else if((now-quietSince).TotalSeconds>=.75){Finished=true;Running=false;}
   }else if(!Finished)quietSince=DateTime.MinValue;
   return changed;
  }
 }
}
