using System;
using System.Text;

namespace ProgressPeek {
 public static class BugReport {
  public const int MaximumTitleLength=150,MaximumFieldLength=4000;
  public static string Limit(string value,int maximum){
   value=(value??"").Replace("\r\n","\n").Replace('\r','\n').Replace("\0","").Trim();
   if(value.Length<=maximum)return value;
   int length=maximum;if(length>0&&Char.IsHighSurrogate(value[length-1]))length--;
   return value.Substring(0,length);
  }
  public static string Format(string title,string happened,string expected,string steps,bool includeDiagnostics,string safeDiagnostics){
   title=Limit(title,MaximumTitleLength).Replace('\n',' ');
   var report=new StringBuilder("# "+(title.Length==0?"Bug report":title)+"\n\n");
   report.Append("## What happened\n").Append(Limit(happened,MaximumFieldLength)).Append("\n\n");
   report.Append("## What I expected\n").Append(Limit(expected,MaximumFieldLength)).Append("\n\n");
   report.Append("## Steps to reproduce\n").Append(Limit(steps,MaximumFieldLength)).Append('\n');
   // This parameter must be a captured allowlisted BetaDiagnostics report, never raw scan data.
   if(includeDiagnostics&&!String.IsNullOrEmpty(safeDiagnostics))report.Append("\n## Diagnostics (reviewed)\n").Append(safeDiagnostics);
   return report.ToString();
  }
  public static string BuildIssueUrl(string repository,string title){
   string endpoint=DistributionConfig.GetIssuesUrl(repository);title=Limit(title,MaximumTitleLength).Replace('\n',' ');
   return endpoint.Length==0||String.IsNullOrWhiteSpace(title)?"":endpoint+"?title="+Uri.EscapeDataString(title);
  }
 }
}
