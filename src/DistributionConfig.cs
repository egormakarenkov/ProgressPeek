using System;
using System.Text.RegularExpressions;

namespace ProgressPeek {
 public static class DistributionConfig {
  // Publisher configuration is compiled into the application; user settings cannot change trust.
  public const string Repository="";
  public const string UpdatePublicKeyXml="";
  public static bool IsRepositoryConfigured {get{return IsValidRepository(Repository);}}
  public static string IssuesUrl {get{return GetIssuesUrl(Repository);}}
  public static bool IsValidRepository(string value){
   if(String.IsNullOrEmpty(value)||value.Length>140)return false;
   string[] parts=value.Split('/');
   if(parts.Length!=2||parts[0].Contains("--")||parts[1]=="."||parts[1]=="..")return false;
   return Regex.IsMatch(parts[0],@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z")&&Regex.IsMatch(parts[1],@"\A[A-Za-z0-9_.-]{1,100}\z");
  }
  public static string GetIssuesUrl(string repository){return IsValidRepository(repository)?"https://github.com/"+repository+"/issues/new":"";}
 }
}
