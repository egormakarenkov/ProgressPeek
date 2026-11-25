using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProgressPeek;

public static class ReaderFlowTests {
 class Fixture {public MessageNode Info;public Fixture Last,Previous;public Fixture(string kind,string name){Info=N(kind,name);}public void Add(Fixture node){node.Previous=Last;Last=node;}}
 static MessageNode N(string kind,string name){return new MessageNode(kind,name);}
 static Type Reader {get{return typeof(Monitor).Assembly.GetType("ProgressPeek.ReaderFlow");}}
 static Snapshot Read(params MessageNode[] nodes){return (Snapshot)Reader.GetMethod("ReadFlat").Invoke(null,new object[]{nodes,true,false});}
 public static void Run(Action<bool,string> check){
  check(Reader!=null,"reader exposes deterministic response extraction");if(Reader==null)return;
  var separated=Reader.GetMethod("ReadParts");check(separated!=null,"reader uses separate structural prompt and response spans");if(separated!=null){var literal=(Snapshot)separated.Invoke(null,new object[]{new[]{N("Text","Explain transcript format")},new[]{N("Text","Example"),N("Text","You said:"),N("Text","Quoted user message"),N("Button","Copy response")},true,true,false});check(literal.Prompt=="Explain transcript format"&&literal.Output.Contains("Quoted user message")&&literal.FinalVisible,"literal user-role text inside response cannot replace selected prompt");}
  var s=Read(N("Text","You said:"),N("Text","Original request"),N("Text","ChatGPT said:"),N("Text","Working for 10s"),N("Text","You said:"),N("Text","Keep the first requirement"),N("Button","Copy message"),N("Text","ChatGPT said:"),N("Text","Thinking"));
  check(s.Prompt=="Keep the first requirement"&&!s.PriorResponseFinished&&s.Generating,"latest sent followup remains visible while preceding response runs");
  s=Read(N("Text","You said:"),N("Text","Old"),N("Text","ChatGPT said:"),N("Text","Done"),N("Button","Copy response"),N("Text","You said:"),N("Text","Next"),N("Text","ChatGPT said:"),N("Text","Thinking"));
  check(s.PriorResponseFinished&&!s.FinalVisible&&s.Prompt=="Next","only immediately previous response footer proves preceding turn finished");
  s=Read(N("Text","You said:"),N("Text","Old"),N("Text","ChatGPT said:"),N("Button","Copy response"),N("Text","You said:"),N("Text","Middle"),N("Text","ChatGPT said:"),N("Text","Working for 2s"),N("Text","You said:"),N("Text","Followup"),N("Text","ChatGPT said:"),N("Text","Still working"));
  check(!s.PriorResponseFinished&&!s.FinalVisible,"older history footer cannot finish latest response or split active followup scope");
  s=Read(N("Text","You said:"),N("Text","Question"),N("Button","Copy message"),N("Text","ChatGPT said:"),N("Text","Answer"));
  check(!s.FinalVisible,"user bubble Copy message is never assistant completion");
  foreach(string terminal in new[]{"You stopped this response","Error generating response","Waiting for input"}){
   s=Read(N("Text","You said:"),N("Text","Question"),N("Text","ChatGPT said:"),N("Text","Answer"),N("Text",terminal),N("Button","Copy response"));
   check(!s.FinalVisible&&s.Terminal.Length>0,"terminal safety overrides response footer: "+terminal);
  }
  s=(Snapshot)Reader.GetMethod("ReadFlat").Invoke(null,new object[]{new[]{N("Text","You said:"),N("Text","Question"),N("Text","ChatGPT said:"),N("Text","Answer"),N("Button","Copy response")},true,true});
  check(s.Generating&&!s.FinalVisible,"composer Stop is authoritative over final footer");
  s=Read(N("Text","You said:"),N("Text","Question"),N("Text","ChatGPT said:"),N("Text","Answer"),N("Button","Copy response"),N("Edit","Unsent draft"),N("Text","Unsent attachment"));
  check(!s.Output.Contains("Unsent")&&!s.Prompt.Contains("Unsent"),"draft and attachment content after composer excluded");
  var longPrompt="Original critical requirement: "+new string('x',14000)+" end";
  s=Read(N("Text","You said:"),N("Text",longPrompt),N("Text","ChatGPT said:"),N("Text","Answer"));
  check(s.Prompt.StartsWith("Original critical requirement:")&&s.Prompt.Length<=12000&&s.Truncated,"oversized prompt retains original requirement at head");
  check(s.Prompt.EndsWith(" end"),"oversized prompt retains final requirements as well as beginning");
  var longResponse=new List<MessageNode>{N("Text","You said:"),N("Text","Question"),N("Text","ChatGPT said:")};
  for(int i=0;i<2000;i++)longResponse.Add(N("Text","Tool detail "+i));
  longResponse.Add(N("Text","Final answer"));longResponse.Add(N("Button","Copy response"));
  var scan=Reader.GetMethod("ReadFlat");s=(Snapshot)scan.Invoke(null,new object[]{longResponse,true,false});
  check(s.FinalVisible&&s.ResponseReadComplete&&s.Truncated&&s.Output.Contains("Final answer")&&!s.Output.Contains("Tool detail 0\n"),"bounded long response prioritizes recent output and discovers tail footer");
  longResponse.RemoveAt(longResponse.Count-1);s=(Snapshot)scan.Invoke(null,new object[]{longResponse,true,false});
  check(!s.FinalVisible&&!s.ResponseReadComplete&&s.Truncated,"truncated status without positive footer stays explicitly uncertain");
  var tree=new Fixture("Group","Response");var tools=new Fixture("Group","Tool details");
  for(int i=0;i<2000;i++)tools.Add(new Fixture("Text","Command output "+i));
  tree.Add(tools);tree.Add(new Fixture("Text","Latest final answer"));tree.Add(new Fixture("Button","Copy response"));
  int visits=0;var tail=BoundedNodeReader.Read(new[]{tree},n=>{visits++;return n.Info;},n=>n.Last,n=>n.Previous,true,40,()=>false);
  s=new Snapshot();ReaderFlow.ApplyStatus(s,tail.Nodes,tail.Complete,false);
  check(!tail.Complete&&visits==40&&s.FinalVisible&&s.ResponseReadComplete&&ReaderFlow.Text(tail.Nodes).EndsWith("Latest final answer"),"actual reverse tree walk finds tail footer before bounded tool descendants");
  tree.Add(new Fixture("Button","Approve"));tail=BoundedNodeReader.Read(new[]{tree},n=>n.Info,n=>n.Last,n=>n.Previous,true,40,()=>false);s=new Snapshot();ReaderFlow.ApplyStatus(s,tail.Nodes,tail.Complete,false);
  check(!s.FinalVisible&&s.Terminal=="Awaiting your input","approval after footer is not lost behind long tool subtree");
  tree.Add(new Fixture("Text","You stopped this response"));tail=BoundedNodeReader.Read(new[]{tree},n=>n.Info,n=>n.Last,n=>n.Previous,true,40,()=>false);s=new Snapshot();ReaderFlow.ApplyStatus(s,tail.Nodes,tail.Complete,false);
  check(!s.FinalVisible&&s.Terminal=="Cancelled","cancellation at current tail remains authoritative on bounded scan");
  var draft=new Fixture("Edit","Composer");draft.Add(new Fixture("Text","Unsent attachment"));tail=BoundedNodeReader.Read(new[]{draft},n=>n.Info,n=>n.Last,n=>n.Previous,true,40,()=>false);
  check(tail.Nodes.Count==1&&!ReaderFlow.Text(tail.Nodes).Contains("attachment"),"tree traversal never opens draft descendants");
  tail=BoundedNodeReader.Read(new[]{tree},n=>n.Info,n=>n.Last,n=>n.Previous,true,40,()=>true);s=new Snapshot();ReaderFlow.ApplyStatus(s,tail.Nodes,tail.Complete,false);
  check(!tail.Complete&&!s.ResponseReadComplete&&!s.FinalVisible,"expired traversal budget returns uncertain status rather than false completion");
 }
 public static int Main(){int failed=0,total=0;Run((ok,name)=>{total++;if(!ok){failed++;Console.WriteLine("FAIL: "+name);}});Console.WriteLine("Reader checks: "+total+", failures: "+failed);return failed==0?0:1;}
}
