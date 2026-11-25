using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ProgressPeek;

public static class BugReportTests {
 public static void Run(Action<bool,string> check) {
  var assembly=typeof(Settings).Assembly;
  var config=assembly.GetType("ProgressPeek.DistributionConfig");
  var report=assembly.GetType("ProgressPeek.BugReport");
  check(config!=null,"bug report distribution configuration exists");
  check(report!=null,"local bug report formatter exists");
  var ui=assembly.GetType("ProgressPeek.BugReportUi");
  check(ui!=null,"local bug report owned window exists");
  if(ui!=null){
   var owner=new Window{Width=1,Height=1,Opacity=0,ShowActivated=false,ShowInTaskbar=false};owner.Show();var window=(Window)ui.GetMethod("Create").Invoke(null,new object[]{owner,new Settings(),"SAFE_UI_DIAGNOSTICS"});
   try{
    Func<string,object> field=name=>window.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(window);
    var preview=(TextBox)field("Preview");var titleInput=(TextBox)field("TitleInput");var diagnostics=(CheckBox)field("IncludeDiagnostics");var continueButton=(Button)field("Continue");
    check(window.Owner==owner,"report window keeps the supplied owner for dialog click-away behavior");
    check(diagnostics.IsChecked==false&&!preview.Text.Contains("SAFE_UI_DIAGNOSTICS"),"report window starts with diagnostics excluded");
    titleInput.Text="Edited title";((TextBox)field("HappenedInput")).Text="Stopped updating";
    check(preview.IsReadOnly&&preview.Text.Contains("# Edited title")&&preview.Text.Contains("Stopped updating"),"editing fields refreshes the exact read-only report preview");
    diagnostics.IsChecked=true;check(preview.Text.EndsWith("## Diagnostics (reviewed)\nSAFE_UI_DIAGNOSTICS",StringComparison.Ordinal),"opting in adds the captured diagnostics exactly to the report preview");
    diagnostics.IsChecked=false;check(!preview.Text.Contains("SAFE_UI_DIAGNOSTICS"),"opting out removes diagnostics from report preview");
    check(!continueButton.IsEnabled&&((TextBlock)field("Status")).Text.Contains("Not configured"),"unconfigured report window honestly disables GitHub continuation");
   }finally{window.Close();owner.Close();}
  }
  if(config==null||report==null)return;
  Func<string,bool> valid=value=>(bool)config.GetMethod("IsValidRepository").Invoke(null,new object[]{value});
  foreach(string value in new[]{null,"","https://github.com/owner/repo","owner/repo?x=1","owner/repo#fragment","owner/repo/extra","owner\\repo"," owner/repo","owner/repo ","öwner/repo","-owner/repo","owner-/repo","owner--x/repo","owner/.","owner/..",new string('a',40)+"/repo","owner/"+new string('r',101)})check(!valid(value),"repository rejects unsafe or malformed syntax: "+(value??"null"));
  foreach(string value in new[]{"owner/repo","Owner-1/repo.name_2-3","a/x"})check(valid(value),"repository accepts ASCII owner/repo: "+value);
  check(!(bool)config.GetProperty("IsRepositoryConfigured").GetValue(null,null)&&String.IsNullOrEmpty((string)config.GetProperty("IssuesUrl").GetValue(null,null)),"unconfigured distribution cannot launch an issue form");
  var repository=config.GetField("Repository");var key=config.GetField("UpdatePublicKeyXml");
  check(repository!=null&&repository.IsLiteral&&key!=null&&key.IsLiteral,"publisher repository and update trust are immutable compiled configuration");
  var format=report.GetMethod("Format");
  Func<string,string,string,string,bool,string,string> build=(title,happened,expected,steps,include,diagnostics)=>(string)format.Invoke(null,new object[]{title,happened,expected,steps,include,diagnostics});
  const string safe="SAFE_DIAGNOSTIC_FIXTURE\nVisible nodes: 55\n";
  string local=build("A title","It stopped","It updates","1. Open widget",false,safe);
  check(local.Contains("# A title")&&local.Contains("## What happened\nIt stopped")&&local.Contains("## What I expected\nIt updates")&&local.Contains("## Steps to reproduce\n1. Open widget"),"report formats each user field into readable Markdown");
  check(!local.Contains("SAFE_DIAGNOSTIC_FIXTURE")&&!local.Contains("Diagnostics"),"optional diagnostics are entirely omitted by default");
  string included=build("A title","It stopped","It updates","1. Open widget",true,safe);
  check(included.EndsWith("## Diagnostics (reviewed)\n"+safe,StringComparison.Ordinal)&&included.IndexOf(safe,StringComparison.Ordinal)==included.LastIndexOf(safe,StringComparison.Ordinal),"enabled diagnostics preserve the exact previewed safe report once");
  string bounded=build(new string('t',1000),new string('h',9000),new string('e',9000),new string('s',9000),false,safe);
  check(!bounded.Contains(new string('t',151))&&!bounded.Contains(new string('h',4001))&&!bounded.Contains(new string('e',4001))&&!bounded.Contains(new string('s',4001)),"user fields have hard report limits");
  check(build(null,null,null,null,false,null).Contains("# Bug report"),"blank local reports remain copyable and saveable");
  const string privateValue="private prompt user@example.test sk-private C:\\Users\\Private\\auth.json";
  var snapshot=new Snapshot{Chat=privateValue,Prompt=privateValue,Output=privateValue,Error=privateValue,NodeCount=55};
  string sanitized=BetaDiagnostics.Report(new Settings(),snapshot,privateValue);
  check(!build("Bug","Stopped","Working","Open",true,sanitized).Contains(privateValue)&&!sanitized.Contains("sk-private")&&!sanitized.Contains("user@example"),"report diagnostics use the existing safe allowlist rather than raw snapshot data");
  var issue=report.GetMethod("BuildIssueUrl");
  string url=(string)issue.Invoke(null,new object[]{"owner/repo","a & b? # 😃"});
  check(url=="https://github.com/owner/repo/issues/new?title=a%20%26%20b%3F%20%23%20%F0%9F%98%83"&&!url.Contains("body="),"issue URL carries encoded title only and no report body");
  check(String.IsNullOrEmpty((string)issue.Invoke(null,new object[]{"","title"}))&&String.IsNullOrEmpty((string)issue.Invoke(null,new object[]{"owner/repo","  "})),"issue continuation requires a configured repository and title");
 }
 [STAThread] public static int Main(){int failed=0;Run((ok,name)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failed++;});return failed==0?0:1;}
}
