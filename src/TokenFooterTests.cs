using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ProgressPeek;
static class TokenFooterTests {
 static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Set(object card,string name,object value){card.GetType().GetField(name,Flags).SetValue(card,value);}
 static void Call(object card,string name,params object[] args){card.GetType().GetMethod(name,Flags).Invoke(card,args);}
 public static void Run(Action<bool,string> check,string folder){
  var option=typeof(Settings).GetField("TokenDisplay");check(option!=null,"token display preference exists");if(option==null)return;
  var cache=new SessionCache();var pending=new TokenTally();cache.Remember("pending",new SessionState{Tokens=pending});pending.Total=75;check(cache.Find("pending")!=null&&cache.Find("pending").Tokens.Total==75,"task cache retains late reported tokens without an accepted estimate");
  var store=new Storage(Path.Combine(folder,"token-footer"));check((string)option.GetValue(store.Settings)=="Task","widget token footer defaults to current task");option.SetValue(store.Settings,"Chat");store.SaveSettings();check((string)option.GetValue(new Storage(store.Folder).Settings)=="Chat","token scope preference survives restart");
  var card=new Card(store,true);try{
   var tally=new TokenTally();Set(card,"taskTokens",tally);var a=new Snapshot{Chat="A",Prompt="First"};Set(card,"current",a);Set(card,"inputTokens",100L);Set(card,"outputTokens",20L);Call(card,"RecordTokenUsage",a,new CliResult{InputTokens=100,OutputTokens=20},tally);Call(card,"RecordTokenUsage",new Snapshot{Chat="A",Prompt="Second"},new CliResult{InputTokens=200,OutputTokens=30},new TokenTally());Call(card,"RecordTokenUsage",new Snapshot{Chat="B",Prompt="Other"},new CliResult{InputTokens=999},new TokenTally());Call(card,"RenderUsage");var footer=(TextBlock)card.GetType().GetField("usageText",Flags).GetValue(card);
   check(footer.Text.Contains("350")&&footer.Text.Contains("Widget chat"),"chat tokens sum estimates across tasks but exclude other chats");option.SetValue(store.Settings,"Task");Call(card,"RenderUsage");check(footer.Text.Contains("120")&&footer.Text.Contains("Widget task"),"task token mode uses this task only");
   Call(card,"RecordTokenUsage",a,new CliResult{InputTokens=10},tally);Call(card,"RenderUsage");check(footer.Text.Contains("130"),"reported tokens count independently of accepting the estimate");
   var nextTask=new TokenTally();Set(card,"taskTokens",nextTask);Call(card,"RecordTokenUsage",a,new CliResult{InputTokens=5},tally);Call(card,"RenderUsage");check(nextTask.Total==0&&tally.Total==135,"late result belongs to its captured task, not a new identical prompt");Set(card,"taskTokens",tally);
   store.Settings.ShowUsage=false;Call(card,"RenderUsage");check(footer.Visibility==Visibility.Visible&&!footer.Text.Contains("Weekly"),"token footer remains visible when plan usage is hidden");option.SetValue(store.Settings,"Hidden");Call(card,"RenderUsage");check(footer.Visibility==Visibility.Collapsed,"footer hides when both usage and tokens are disabled");
   check(((string)footer.ToolTip).Contains("Cancelled")&&((string)footer.ToolTip).Contains("restart"),"counter explains missing cancelled usage and session lifetime");
  }finally{card.Close();}
 }
}
