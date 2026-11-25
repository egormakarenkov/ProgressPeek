using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace ProgressPeek {
 public sealed class UpdateCandidate {public VerifiedUpdate Manifest;public byte[] ManifestBytes,Signature;public string Tag,AssetUrl,Notes;}
 public interface IUpdateTransport {byte[] Read(Uri uri,int limit,CancellationToken cancel,Func<Uri,bool> redirectAllowed);}
 public sealed class UpdateHttpTransport:IUpdateTransport {
  public byte[] Read(Uri uri,int limit,CancellationToken cancel,Func<Uri,bool> redirectAllowed){
   // TLS 1.2 is available on supported patched Windows; never fall back to SSL/TLS 1.0.
   ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;
   DateTime deadline=DateTime.UtcNow.AddSeconds(45);
   for(int redirects=0;redirects<=3;redirects++){
    cancel.ThrowIfCancellationRequested();if(DateTime.UtcNow>=deadline)throw new TimeoutException();var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;request.UserAgent="ProgressPeek-Updater";request.Accept="application/vnd.github+json";request.UseDefaultCredentials=false;request.Credentials=null;request.Proxy=null;
    using(cancel.Register(()=>request.Abort()))using(var timer=new Timer(s=>request.Abort(),null,Math.Max(1,(int)(deadline-DateTime.UtcNow).TotalMilliseconds),Timeout.Infinite))using(var response=(HttpWebResponse)request.GetResponse()){
     int status=(int)response.StatusCode;if(status>=300&&status<400){Uri next;if(redirects==3||!Uri.TryCreate(uri,response.Headers["Location"],out next)||redirectAllowed==null||!redirectAllowed(next))throw new InvalidDataException("Unsupported update redirect.");uri=next;continue;}
     if(status!=200||response.ContentLength>limit)throw new InvalidDataException("Update response is unavailable or too large.");using(var input=response.GetResponseStream())using(var output=new MemoryStream()){byte[] buffer=new byte[16384];int count;while((count=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();if(DateTime.UtcNow>=deadline)throw new TimeoutException();if(output.Length+count>limit)throw new InvalidDataException("Update response exceeds its limit.");output.Write(buffer,0,count);}return output.ToArray();}
    }
   }throw new InvalidDataException("Too many update redirects.");
  }
 }
 public sealed class UpdateClient {
  readonly string repository,key,current;readonly IUpdateTransport transport;
  internal UpdateClient(string repository,string key,string current,IUpdateTransport transport){this.repository=repository;this.key=key;this.current=current;this.transport=transport;}
  public UpdateClient():this(DistributionConfig.Repository,DistributionConfig.UpdatePublicKeyXml,BetaDiagnostics.Version,new UpdateHttpTransport()){}
  bool Configured {get{return UpdateSecurity.IsConfigured(repository,key);}}
  byte[] Asset(string url,string tag,string name,int limit,CancellationToken cancel){Uri uri=UpdateSecurity.ValidateUrl(url,repository,tag,name,false);return transport.Read(uri,limit,cancel,next=>{try{UpdateSecurity.ValidateUrl(next.AbsoluteUri,repository,tag,name,true);return true;}catch{return false;}});}
  static string StringValue(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)?v as string:null;}
  public UpdateCandidate Discover(bool beta,CancellationToken cancel){
   if(!Configured)return null;string endpoint="https://api.github.com/repos/"+repository+"/releases"+(beta?"?per_page=30":"/latest");byte[] data=transport.Read(new Uri(endpoint),512*1024,cancel,null);var parser=new JavaScriptSerializer{MaxJsonLength=512*1024,RecursionLimit=32};object parsed=parser.DeserializeObject(new UTF8Encoding(false,true).GetString(data));object[] releases=beta?parsed as object[]:new[]{parsed};if(releases==null)throw new InvalidDataException("Invalid releases response.");UpdateCandidate best=null;
   foreach(object row in releases){cancel.ThrowIfCancellationRequested();var release=row as Dictionary<string,object>;if(release==null)continue;object draft,pre;if(!release.TryGetValue("draft",out draft)||!(draft is bool)||(bool)draft||!release.TryGetValue("prerelease",out pre)||!(pre is bool)||!beta&&(bool)pre)continue;string tag=StringValue(release,"tag_name");if(String.IsNullOrEmpty(tag)||!tag.StartsWith("v",StringComparison.Ordinal))continue;UpdateVersion version;try{version=UpdateVersion.Parse(tag.Substring(1));}catch{continue;}if(version.Prerelease!=(bool)pre||version.CompareTo(UpdateVersion.Parse(current))<=0)continue;object assetObject;if(!release.TryGetValue("assets",out assetObject))continue;var assets=assetObject as object[];if(assets==null)continue;var names=new Dictionary<string,string>(StringComparer.Ordinal);bool duplicates=false;foreach(object item in assets){var asset=item as Dictionary<string,object>;if(asset==null)continue;string name=StringValue(asset,"name");if(name!=UpdateSecurity.ManifestName&&name!=UpdateSecurity.SignatureName&&name!=UpdateSecurity.ExecutableName)continue;if(names.ContainsKey(name)){duplicates=true;break;}names[name]=StringValue(asset,"browser_download_url");}if(duplicates||names.Count!=3)continue;
    byte[] manifest=Asset(names[UpdateSecurity.ManifestName],tag,UpdateSecurity.ManifestName,UpdateSecurity.ManifestLimit,cancel),signature=Asset(names[UpdateSecurity.SignatureName],tag,UpdateSecurity.SignatureName,UpdateSecurity.SignatureLimit,cancel);var verified=UpdateSecurity.VerifyManifest(manifest,signature,repository,key,current,beta);if("v"+verified.Version!=tag)throw new InvalidDataException("Signed version and release tag differ.");UpdateSecurity.ValidateUrl(names[UpdateSecurity.ExecutableName],repository,tag,UpdateSecurity.ExecutableName,false);string notes=StringValue(release,"body")??"";notes=notes.Length>8000?notes.Substring(0,8000):notes;var candidate=new UpdateCandidate{Manifest=verified,ManifestBytes=manifest,Signature=signature,Tag=tag,AssetUrl=names[UpdateSecurity.ExecutableName],Notes=notes};if(best==null||UpdateVersion.Parse(verified.Version).CompareTo(UpdateVersion.Parse(best.Manifest.Version))>0)best=candidate;
   }return best;
  }
  public byte[] Download(UpdateCandidate candidate,CancellationToken cancel){if(!Configured||candidate==null)throw new InvalidDataException("Updates are not configured.");var verified=UpdateSecurity.VerifyManifest(candidate.ManifestBytes,candidate.Signature,repository,key,current,true);byte[] bytes=Asset(candidate.AssetUrl,"v"+verified.Version,UpdateSecurity.ExecutableName,(int)verified.Size,cancel);if(bytes.LongLength!=verified.Size||UpdateSecurity.Hash(bytes)!=verified.Sha256)throw new InvalidDataException("Downloaded executable did not match the signed release.");return bytes;}
 }
}
