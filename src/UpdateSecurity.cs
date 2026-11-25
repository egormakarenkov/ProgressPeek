using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace ProgressPeek {
 public sealed class UpdateVersion:IComparable<UpdateVersion> {
  readonly int major,minor,patch,beta;public readonly bool Prerelease;
  UpdateVersion(int a,int b,int c,int d,bool pre){major=a;minor=b;patch=c;beta=d;Prerelease=pre;}
  public static UpdateVersion Parse(string value){var m=Regex.Match(value??"",@"\A(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-beta(?:\.(0|[1-9][0-9]{0,8}))?)?\z");if(!m.Success)throw new InvalidDataException("Unsupported release version.");return new UpdateVersion(Int32.Parse(m.Groups[1].Value),Int32.Parse(m.Groups[2].Value),Int32.Parse(m.Groups[3].Value),m.Groups[4].Success?Int32.Parse(m.Groups[4].Value):0,value.Contains("-beta"));}
  public int CompareTo(UpdateVersion other){int c=major.CompareTo(other.major);if(c==0)c=minor.CompareTo(other.minor);if(c==0)c=patch.CompareTo(other.patch);if(c==0)c=other.Prerelease.CompareTo(Prerelease);if(c==0&&Prerelease)c=beta.CompareTo(other.beta);return c;}
 }
 public sealed class VerifiedUpdate {
  public string Version {get;private set;}public string Channel {get;private set;}public string Sha256 {get;private set;}public long Size {get;private set;}
  internal VerifiedUpdate(string version,string channel,long size,string hash){Version=version;Channel=channel;Size=size;Sha256=hash;}
 }
 public static class UpdateSecurity {
  public const int ManifestLimit=4096,SignatureLimit=1024;public const long PackageLimit=32*1024*1024;public const string ManifestName="ProgressPeek.update.json",SignatureName="ProgressPeek.update.sig",ExecutableName="ProgressPeek.exe";
  public static bool Configured {get{return IsConfigured(DistributionConfig.Repository,DistributionConfig.UpdatePublicKeyXml);}}
  internal static bool IsConfigured(string repository,string key){try{if(!DistributionConfig.IsValidRepository(repository)||String.IsNullOrEmpty(key))return false;using(var rsa=PublicKey(key))return rsa.KeySize>=2048;}catch{return false;}}
  static RSACryptoServiceProvider PublicKey(string xml){if(String.IsNullOrEmpty(xml)||xml.Length>8192||xml.Contains("<D>")||xml.Contains("<P>"))throw new InvalidDataException("Invalid publisher key.");var rsa=new RSACryptoServiceProvider(new CspParameters{ProviderType=24});rsa.PersistKeyInCsp=false;try{rsa.FromXmlString(xml);if(rsa.KeySize<2048)throw new InvalidDataException("Publisher key too small.");return rsa;}catch{rsa.Dispose();throw;}}
  public static string Hash(byte[] bytes){using(var h=SHA256.Create())return Hex(h.ComputeHash(bytes));}
  static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
  public static string HashFile(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var h=SHA256.Create())return Hex(h.ComputeHash(stream));}
  public static VerifiedUpdate VerifyManifest(byte[] bytes,byte[] signature,string repository,string publicKey,string current,bool includeBeta){
   if(bytes==null||bytes.Length==0||bytes.Length>ManifestLimit||signature==null||signature.Length==0||signature.Length>SignatureLimit||!DistributionConfig.IsValidRepository(repository))throw new InvalidDataException("Invalid signed release metadata.");
   using(var rsa=PublicKey(publicKey)){if(!rsa.VerifyData(bytes,"SHA256",signature))throw new InvalidDataException("Release signature could not be verified.");}
   string json=new UTF8Encoding(false,true).GetString(bytes);
   // Exact canonical JSON intentionally rejects duplicate keys, alternate encodings, and unknown fields.
   var m=Regex.Match(json,@"\A\{""app"":""ProgressPeek"",""repository"":""([A-Za-z0-9_.\-/]+)"",""version"":""([0-9a-z.\-]+)"",""channel"":""(stable|beta)"",""filename"":""ProgressPeek.exe"",""size"":([1-9][0-9]{0,8}),""sha256"":""([a-f0-9]{64})""\}\z");
   if(!m.Success||m.Groups[1].Value!=repository)throw new InvalidDataException("Release metadata does not match this publisher.");long size=Int64.Parse(m.Groups[4].Value,CultureInfo.InvariantCulture);if(size>PackageLimit)throw new InvalidDataException("Release file exceeds the size limit.");
   var version=UpdateVersion.Parse(m.Groups[2].Value);if(version.Prerelease!=(m.Groups[3].Value=="beta")||version.Prerelease&&!includeBeta||version.CompareTo(UpdateVersion.Parse(current))<=0)throw new InvalidDataException("Release is not newer or is outside the selected channel.");
   return new VerifiedUpdate(m.Groups[2].Value,m.Groups[3].Value,size,m.Groups[5].Value);
  }
  public static void VerifyFile(string path,VerifiedUpdate manifest){SafePath(path);if(manifest==null||new FileInfo(path).Length!=manifest.Size||HashFile(path)!=manifest.Sha256)throw new InvalidDataException("Downloaded executable did not match the signed release.");}
  public static void SafePath(string path){string full=Path.GetFullPath(path);if(full.StartsWith("\\\\",StringComparison.Ordinal)||full.IndexOf(':',2)>=0)throw new InvalidDataException("Unsupported update location.");for(string p=full;!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked update locations are unsupported.");}}
  public static Uri ValidateUrl(string value,string repo,string tag,string name,bool redirect){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0)throw new InvalidDataException("Unsupported release URL.");if(redirect){if(uri.Host!="release-assets.githubusercontent.com"&&uri.Host!="objects.githubusercontent.com"&&uri.Host!="github.com")throw new InvalidDataException("Unexpected download host.");if(uri.Host=="github.com"&&(uri.Query.Length!=0||uri.AbsolutePath!="/"+repo+"/releases/download/"+tag+"/"+name))throw new InvalidDataException("Unexpected release path.");if(uri.Host!="github.com"&&!uri.AbsolutePath.StartsWith("/github-production-release-asset/",StringComparison.Ordinal))throw new InvalidDataException("Unexpected asset redirect.");}else if(uri.Host!="github.com"||uri.Query.Length!=0||uri.AbsolutePath!="/"+repo+"/releases/download/"+tag+"/"+name)throw new InvalidDataException("Unexpected release asset.");return uri;}
 }
}
