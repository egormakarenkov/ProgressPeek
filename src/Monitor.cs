using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Runtime.InteropServices;
namespace ProgressPeek {
 public class Snapshot {public IntPtr Handle;public string MessageIdentity="",TaskIdentity="";public bool ProvenanceKnown,AtLatest;public string Chat="",ChatIdentity="",PanelIdentity="",Prompt="",FollowUps="",Output="",Context="",Error="",Activity="",Terminal="";public bool Generating,Truncated,FinalVisible,PriorResponseFinished,ScopeNormalized,ResponseReadComplete=true;public int NodeCount;public Snapshot Clone(){return (Snapshot)MemberwiseClone();}}
 public static class Native {
  [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll",EntryPoint="GetWindowLong")]public static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLong")]public static extern int SetWindowLong(IntPtr hwnd,int index,int value);
  [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
  [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
  public struct RECT {public int Left,Top,Right,Bottom;}
 }

 // The same bounded traversal runs against UI Automation and deterministic fixture trees.
 public class NodeRead {public readonly List<MessageNode> Nodes=new List<MessageNode>();public bool Complete=true;public int Visited;}
 public static class ReaderTailPolicy {
  public static bool IsLatest(bool structuralTail,bool? scrollAtLatest,bool historicalJump){return structuralTail&&!historicalJump&&(!scrollAtLatest.HasValue||scrollAtLatest.Value);}
 }
 public static class BoundedNodeReader {
  public static NodeRead Read<T>(IList<T> roots,Func<T,MessageNode> info,Func<T,T> edge,Func<T,T> sibling,bool reverse,int maxNodes,Func<bool> expired) where T:class {
   var result=new NodeRead();
   Action<T,int> visit=null;visit=(node,depth)=>{
    if(node==null)return;
    if(result.Visited>=maxNodes||expired()){result.Complete=false;return;}
    var data=info(node);result.Visited++;
    if(!reverse)result.Nodes.Add(data);
    if(data.Kind!="Edit"){
     var child=edge(node);
     if(depth>=10&&child!=null)result.Complete=false;
     else while(child!=null){if(result.Visited>=maxNodes||expired()){result.Complete=false;break;}visit(child,depth+1);child=sibling(child);}
    }
    if(reverse)result.Nodes.Add(data);
   };
   for(int j=0;j<roots.Count;j++){if(result.Visited>=maxNodes||expired()){result.Complete=false;break;}visit(roots[reverse?roots.Count-1-j:j],0);}
   if(reverse)result.Nodes.Reverse();return result;
  }
 }
 public static class ReaderFlow {
  public static void ApplyProvenance(Snapshot s,string messageIdentity,bool atLatest){s.ProvenanceKnown=true;s.MessageIdentity=messageIdentity??"";s.AtLatest=atLatest;}
  public static Snapshot ReadParts(IList<MessageNode> prompt,IList<MessageNode> response,bool promptComplete,bool responseComplete,bool composerStop){
   string request=Text(prompt),output=Text(response);var snap=new Snapshot{Prompt=Evidence.BoundRequest(request,12000),Output=Evidence.Bound(output,6000),Truncated=!promptComplete||!responseComplete||request.Length>12000||output.Length>6000};ApplyStatus(snap,response,responseComplete,composerStop);if(String.IsNullOrWhiteSpace(snap.Prompt))snap.Error="Waiting for an accessible user message";return snap;
  }
  public static bool User(MessageNode n){return n.Kind=="Text"&&n.Name=="You said:";}
  public static int LatestRole(IList<MessageNode> nodes){for(int i=nodes.Count-1;i>=0;i--)if(User(nodes[i]))return i;return -1;}
  public static int PromptEnd(IList<MessageNode> nodes,int role){
   int end=role;
   for(int i=role+1;i<nodes.Count;i++){var n=nodes[i];if(User(n)||n.Kind=="Edit"||MessageBoundary.IsChrome(n.Name)||MessageBoundary.IsRunning(new[]{n}))break;end=i;}
   return end;
  }
  public static string Text(IEnumerable<MessageNode> nodes){return String.Join("\n",nodes.Where(n=>n.Kind=="Text"&&!String.IsNullOrWhiteSpace(n.Name)&&!MessageBoundary.IsChrome(n.Name)&&n.Name!="You said:"&&n.Name!="You").Select(n=>n.Name));}
  public static string Head(string text,int limit){return Evidence.BoundRequest(text,limit);}
  public static void ApplyStatus(Snapshot snap,IList<MessageNode> response,bool complete,bool composerStop){
   snap.Terminal="";
   if(response.Any(n=>n.Name=="Response cancelled"||n.Name=="Turn cancelled"||n.Name=="You stopped this response"))snap.Terminal="Cancelled";
   else if(response.Any(n=>n.Name=="Something went wrong"||n.Name=="Error generating response"))snap.Terminal="Failed";
   else if(response.Any(n=>n.Name=="Waiting for input"||n.Name=="Needs input"||n.Kind=="Button"&&(n.Name=="Allow once"||n.Name=="Approve")))snap.Terminal="Awaiting your input";
   bool running=composerStop||MessageBoundary.IsRunning(response);
   snap.Generating=running;
   if(!composerStop&&snap.Terminal.Length>0)snap.Generating=false;
   snap.FinalVisible=!running&&snap.Terminal.Length==0&&MessageBoundary.IsFinished(response);
   // Omitted older body text cannot hide controls newer than the observed tail footer.
   // No marker means an incomplete scan remains unknown, never inferred finished.
   snap.ResponseReadComplete=complete||snap.FinalVisible||snap.Terminal.Length>0;
   if(response.Any(n=>n.Kind=="Text"&&(n.Name=="Thinking"||n.Name=="Thinking…"||n.Name=="Thinking...")))snap.Activity="Thinking";
   else if(response.Any(n=>n.Name=="Running command"||n.Name=="Running commands"||n.Name=="Editing files"))snap.Activity="Coding and running tools";
   else snap.Activity=snap.Generating?"Working":"";
  }
  public static Snapshot ReadFlat(IList<MessageNode> source,bool complete,bool composerStop){
   // Flat boundary data has already excluded the composer branch; Edit is an additional fixture/provider guard.
   int composer=source.ToList().FindIndex(n=>n.Kind=="Edit");var nodes=(composer<0?source:source.Take(composer).ToList());
   var snap=new Snapshot();int role=LatestRole(nodes);
   if(role<0){snap.Error="Waiting for an accessible user message";snap.ResponseReadComplete=false;return snap;}
   int end=PromptEnd(nodes,role);string prompt=Text(nodes.Skip(role+1).Take(end-role));snap.Prompt=Head(prompt,12000);
   var response=nodes.Skip(end+1).ToList();bool bounded=response.Count>900;if(bounded)response=response.Skip(response.Count-900).ToList();
   string output=Text(response);snap.Output=Evidence.Bound(output,6000);snap.Truncated=!complete||bounded||prompt.Length>12000||output.Length>6000;
   ApplyStatus(snap,response,complete&&!bounded,composerStop);
   int previous=-1;for(int i=role-1;i>=0;i--)if(User(nodes[i])){previous=i;break;}
   if(previous>=0){
    int previousEnd=PromptEnd(nodes,previous);var prior=nodes.Skip(previousEnd+1).Take(Math.Max(0,role-previousEnd-1)).ToList();
    var state=new Snapshot();ApplyStatus(state,prior,true,false);snap.PriorResponseFinished=state.FinalVisible;
   }
   if(String.IsNullOrWhiteSpace(snap.Prompt))snap.Error="Waiting for an accessible user message";
   snap.NodeCount=nodes.Count;return snap;
  }
 }
 public class Monitor:IDisposable {
  readonly Action<Snapshot> publish;readonly ManualResetEvent stop=new ManualResetEvent(false);Thread thread;
  public Monitor(Action<Snapshot> publish){this.publish=publish;thread=new Thread(Loop){IsBackground=true,Name="ProgressPeek accessibility"};thread.SetApartmentState(ApartmentState.MTA);thread.Start();}
  void Loop(){while(!stop.WaitOne(250)){Snapshot snapshot;try{snapshot=Read();}catch(Exception e){snapshot=new Snapshot{Error=e is ElementNotAvailableException?"Chat layout changed":"Accessibility unavailable",ResponseReadComplete=false};}if(stop.WaitOne(0))break;publish(snapshot);}}
  static List<AutomationElement> TailChildren(AutomationElement node,int max){var list=new List<AutomationElement>();var walker=TreeWalker.ControlViewWalker;var n=walker.GetLastChild(node);while(n!=null&&list.Count<max){list.Add(n);n=walker.GetPreviousSibling(n);}list.Reverse();return list;}
  static MessageNode Info(AutomationElement node){var a=node.Current;return new MessageNode(a.ControlType.ProgrammaticName.Substring("ControlType.".Length),a.Name??"");}
  static AutomationElement ConversationScope(AutomationElement start){
   var walker=TreeWalker.ControlViewWalker;var node=start;
   for(int depth=0;depth<3&&node!=null;depth++,node=walker.GetParent(node)){
    var info=node.Current;if(info.ControlType!=ControlType.Group||info.AutomationId=="root"||info.AutomationId=="RootWebArea")break;
    if(TailChildren(node,600).Any(x=>ReaderFlow.User(Info(x))))return node;
   }return start;
  }
  static NodeRead ReadNodes(IList<AutomationElement> roots,bool reverse,int max,int milliseconds,Dictionary<AutomationElement,MessageNode> cache){
   var walker=TreeWalker.ControlViewWalker;var timer=Stopwatch.StartNew();
   Func<AutomationElement,MessageNode> metadata=n=>{MessageNode data;if(!cache.TryGetValue(n,out data)){data=Info(n);cache[n]=data;}return data;};
   return BoundedNodeReader.Read(roots,metadata,reverse?(Func<AutomationElement,AutomationElement>)walker.GetLastChild:walker.GetFirstChild,reverse?(Func<AutomationElement,AutomationElement>)walker.GetPreviousSibling:walker.GetNextSibling,reverse,max,()=>timer.ElapsedMilliseconds>milliseconds);
  }
  static bool ComposerStop(AutomationElement composerBranch){
   if(composerBranch==null)return false;
   // Read only exact running-control metadata, never draft text or attachment descendants.
   var condition=new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new OrCondition(new PropertyCondition(AutomationElement.NameProperty,"Stop"),new PropertyCondition(AutomationElement.NameProperty,"Stop generating")));
   foreach(AutomationElement button in composerBranch.FindAll(TreeScope.Descendants,condition))if(!button.Current.IsOffscreen)return true;
   return false;
  }
  static bool AtLatest(AutomationElement scope,bool structuralTail){
   // Virtualized messages alone cannot establish chronology. Require a readable
   // conversation scroll position; inaccessible providers stay explicitly unknown.
   var walker=TreeWalker.ControlViewWalker;var node=scope;
   for(int i=0;i<5&&node!=null;i++,node=walker.GetParent(node)){
    object pattern;if(node.TryGetCurrentPattern(ScrollPattern.Pattern,out pattern)){var scroll=((ScrollPattern)pattern).Current;if(scroll.VerticallyScrollable)return ReaderTailPolicy.IsLatest(structuralTail,scroll.VerticalScrollPercent>=99.9,false);}
   }
   foreach(AutomationElement child in scope.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ScrollBar))){
    object pattern;if(child.TryGetCurrentPattern(RangeValuePattern.Pattern,out pattern)){var range=((RangeValuePattern)pattern).Current;var rect=child.Current.BoundingRectangle;if(rect.Height>rect.Width)return ReaderTailPolicy.IsLatest(structuralTail,range.Maximum>range.Minimum&&range.Value>=range.Maximum-.001,false);}
   }
   var jumpCondition=new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new OrCondition(new PropertyCondition(AutomationElement.NameProperty,"Scroll to bottom"),new PropertyCondition(AutomationElement.NameProperty,"Jump to bottom")));
   // ChatGPT's web provider omits the nested scroller pattern. Its explicit
   // historical-position control is authoritative; a fully read final span
   // directly preceding the composer is the provider's structural tail.
   foreach(AutomationElement jump in scope.FindAll(TreeScope.Descendants,jumpCondition))if(!jump.Current.IsOffscreen)return ReaderTailPolicy.IsLatest(structuralTail,null,true);
   return ReaderTailPolicy.IsLatest(structuralTail,null,false);
  }
  public static Snapshot Read(){
   var foreground=Native.GetForegroundWindow();Process p=null;uint pid;Native.GetWindowThreadProcessId(foreground,out pid);try{var candidate=Process.GetProcessById((int)pid);if(candidate.ProcessName=="ChatGPT")p=candidate;}catch{}if(p==null)p=Process.GetProcessesByName("ChatGPT").FirstOrDefault(x=>x.MainWindowHandle!=IntPtr.Zero);
   if(p==null)return new Snapshot{Error="Open a ChatGPT chat",ResponseReadComplete=false};var handle=p.MainWindowHandle;if(handle==IntPtr.Zero)return new Snapshot{Error="ChatGPT window unavailable",ResponseReadComplete=false};
   var root=AutomationElement.FromHandle(handle);var edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));AutomationElement composer=null;var candidates=new List<AutomationElement>();var candidateInfo=new List<ComposerInfo>();foreach(AutomationElement edit in edits){var info=edit.Current;var name=info.Name;if(name=="Work with ChatGPT"||name=="Ask ChatGPT"||name=="Ask anything"||name=="Do anything"){candidates.Add(edit);candidateInfo.Add(new ComposerInfo(info.IsOffscreen,!info.BoundingRectangle.IsEmpty&&info.BoundingRectangle.Width>0&&info.BoundingRectangle.Height>0));}}int selected=MessageBoundary.SelectComposer(candidateInfo);if(selected>=0)composer=candidates[selected];
   if(composer==null)return new Snapshot{Handle=handle,Error="Current chat composer unavailable",ResponseReadComplete=false};
   var walker=TreeWalker.ControlViewWalker;var parent=walker.GetParent(composer);var scope=parent==null?null:walker.GetParent(parent);if(scope==null||scope.Current.ControlType!=ControlType.Group||scope.Current.AutomationId=="RootWebArea")return new Snapshot{Handle=handle,Error="Current chat scope unavailable",ResponseReadComplete=false};
   scope=ConversationScope(scope);var composerBranch=MessageBoundary.ComposerBranch(composer,scope,x=>walker.GetParent(x),(a,b)=>Automation.Compare(a,b));
   var direct=TailChildren(scope,600);bool directTruncated=direct.Count>=600;
   int composerIndex=composerBranch==null?-1:direct.FindIndex(n=>Automation.Compare(n,composerBranch));if(composerIndex>=0)direct=direct.Take(composerIndex).ToList();
   var cache=new Dictionary<AutomationElement,MessageNode>();var messageInfo=new List<MessageNode>();foreach(var element in direct){var data=Info(element);cache[element]=data;messageInfo.Add(data);}
   int role=ReaderFlow.LatestRole(messageInfo);bool composerRunning=ComposerStop(composerBranch);
   if(role<0)return new Snapshot{Handle=handle,Generating=composerRunning,Activity=composerRunning?"Working":"",Error="Waiting for an accessible user message",ResponseReadComplete=false};
   int end=ReaderFlow.PromptEnd(messageInfo,role);
   var prompt=ReadNodes(direct.Skip(role+1).Take(end-role).ToList(),false,400,180,cache);
   var response=ReadNodes(direct.Skip(end+1).ToList(),true,900,350,cache);
   // Only the latest two roles matter for deciding whether a newly sent message begins a new response.
   int previous=-1;for(int i=role-1;i>=0;i--)if(ReaderFlow.User(messageInfo[i])){previous=i;break;}
   NodeRead prior=null;
   if(previous>=0){
    int priorEnd=ReaderFlow.PromptEnd(messageInfo,previous);
    prior=ReadNodes(direct.Skip(priorEnd+1).Take(Math.Max(0,role-priorEnd-1)).ToList(),true,120,70,cache);
   }
   var snap=ReaderFlow.ReadParts(prompt.Nodes,response.Nodes,prompt.Complete,response.Complete,composerRunning);
   ReaderFlow.ApplyProvenance(snap,String.Join(".",direct[role].GetRuntimeId().Select(x=>x.ToString())),AtLatest(scope,composerIndex>=0&&role>=0&&end>=role&&end<direct.Count&&composerIndex==direct.Count));
   // Direct status rows are cheap and remain visible even when a tool body exceeds the traversal budget.
   var responseStatus=response.Nodes.Concat(messageInfo.Skip(end+1)).ToList();ReaderFlow.ApplyStatus(snap,responseStatus,response.Complete,composerRunning);
   if(prior!=null){int priorEnd=ReaderFlow.PromptEnd(messageInfo,previous);var priorState=new Snapshot();ReaderFlow.ApplyStatus(priorState,prior.Nodes.Concat(messageInfo.Skip(priorEnd+1).Take(Math.Max(0,role-priorEnd-1))).ToList(),prior.Complete,false);snap.PriorResponseFinished=priorState.FinalVisible;}
   snap.Handle=handle;snap.ChatIdentity=String.Join(".",scope.GetRuntimeId().Select(x=>x.ToString()));
   var identityNode=scope;for(int i=0;i<4&&identityNode!=null;i++,identityNode=walker.GetParent(identityNode)){var info=identityNode.Current;if(info.AutomationId.EndsWith("-chat-panel",StringComparison.Ordinal)){snap.PanelIdentity=info.AutomationId;break;}if(info.AutomationId=="RootWebArea"||info.AutomationId=="root")break;if(snap.PanelIdentity.Length==0&&!String.IsNullOrEmpty(info.AutomationId))snap.PanelIdentity=info.AutomationId;}
   var doc=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"RootWebArea"));if(doc!=null)snap.Chat=doc.Current.Name;
   // Context reuses the current scan; it no longer walks the full response twice more.
   int first=messageInfo.FindIndex(ReaderFlow.User);string original="";if(first>=0){int originalEnd=ReaderFlow.PromptEnd(messageInfo,first);original=ReaderFlow.Text(messageInfo.Skip(first+1).Take(originalEnd-first));}
   snap.Context=ChatContext.Build(original,snap.Prompt+"\n"+snap.Output);
   snap.NodeCount=cache.Count;snap.Truncated|=directTruncated||!prompt.Complete||!response.Complete||prior!=null&&!prior.Complete||original.Length>2500||snap.Prompt.Length+snap.Output.Length>4000;
   return snap;
  }
  public void Dispose(){stop.Set();}
 }
}
