import subprocess,json,sys,base64,re
from pathlib import Path
root=Path(__file__).parent
pid=int(sys.argv[1]);window=int(sys.argv[2]);label=sys.argv[3] if len(sys.argv)>3 else None;name=sys.argv[4] if len(sys.argv)>4 else "ui"
proc=subprocess.Popen(["cua-driver","mcp"],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,encoding="utf-8")
seq=0
def rpc(method,params):
 global seq
 seq+=1;proc.stdin.write(json.dumps({"jsonrpc":"2.0","id":seq,"method":method,"params":params})+"\n");proc.stdin.flush()
 while True:
  line=proc.stdout.readline()
  if not line:raise RuntimeError("cua MCP连接结束")
  try:d=json.loads(line)
  except ValueError:continue
  if d.get("id")==seq:
   if "error" in d:raise RuntimeError(d["error"])
   return d["result"]
def call(tool,args):
 d=rpc("tools/call",{"name":tool,"arguments":args})
 if "structuredContent" in d:return d["structuredContent"]
 for item in d.get("content",[]):
  if item.get("type")=="text":
   try:return json.loads(item["text"])
   except ValueError:pass
 return d
rpc("initialize",{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"扫描批次验收","version":"1.0"}})
proc.stdin.write(json.dumps({"jsonrpc":"2.0","method":"notifications/initialized"})+"\n");proc.stdin.flush()
d=call("get_window_state",{"pid":pid,"window_id":window,"max_elements":6000});tree=d.get("tree_markdown","");(root/(name+"-before.json")).write_text(json.dumps(d,ensure_ascii=False),encoding="utf-8")
if label:
 matches=list(re.finditer(r"\[(\d+)\] Button "+re.escape('"'+label+'"')+r"[^\n]*",tree))
 m=next((match for match in matches if len(sys.argv)<6 or sys.argv[5] in match[0]),None)
 if not m:raise RuntimeError("未找到按钮："+label+"\n"+tree[-1200:])
 print(call("click",{"pid":pid,"window_id":window,"element_token":d["snapshot_id"]+":"+m[1],"delivery_mode":"background"}))
 import time;time.sleep(.5)
 d=call("get_window_state",{"pid":pid,"window_id":window,"max_elements":6000})
(root/(name+".json")).write_text(json.dumps(d,ensure_ascii=False),encoding="utf-8")
if d.get("screenshot_png_b64"):(root/(name+".png")).write_bytes(base64.b64decode(d["screenshot_png_b64"]))
print(d.get("tree_markdown",str(d))[-4300:]);proc.terminate()
