import socket,json,sys
from pathlib import Path
root=Path(__file__).parent
port=int(sys.argv[1]);request={"command":sys.argv[2]}
if len(sys.argv)>3 and sys.argv[3]!="-":request["text"]=sys.argv[3]
if request["command"]=="capture":request["path"]=str((root/"runtime"/sys.argv[3]).resolve());request.pop("text",None)
with socket.create_connection(("127.0.0.1",port),timeout=60) as s:
 s.sendall((json.dumps(request,ensure_ascii=False)+"\n").encode());d=json.loads(s.makefile("r",encoding="utf-8").readline())
if len(sys.argv)>4:(root/sys.argv[4]).write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding="utf-8")
probe=d.get("data",{});probe=probe.get("probe",probe) if isinstance(probe,dict) else probe
if isinstance(probe,dict) and "result" in probe:
 result=probe["result"];print({k:result.get(k) for k in ["ok","partial","registered_this_call","scope_count","errors","empty_batch","applied"] if k in result})
else:print(str(d)[:2100])
