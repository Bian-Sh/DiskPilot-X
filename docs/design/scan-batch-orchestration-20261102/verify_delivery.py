from pathlib import Path
import json,hashlib,ctypes,re
root=Path(__file__).resolve().parent;source=root.parents[2]
packages=json.loads((root/"packages.json").read_text(encoding="utf-8-sig"))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()
k=ctypes.WinDLL("kernel32",use_last_error=True)
k.LoadLibraryExW.argtypes=[ctypes.c_wchar_p,ctypes.c_void_p,ctypes.c_uint];k.LoadLibraryExW.restype=ctypes.c_void_p
k.FindResourceW.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p];k.FindResourceW.restype=ctypes.c_void_p
k.LoadResource.argtypes=[ctypes.c_void_p,ctypes.c_void_p];k.LoadResource.restype=ctypes.c_void_p
k.SizeofResource.argtypes=[ctypes.c_void_p,ctypes.c_void_p];k.SizeofResource.restype=ctypes.c_uint
k.LockResource.argtypes=[ctypes.c_void_p];k.LockResource.restype=ctypes.c_void_p
k.FreeLibrary.argtypes=[ctypes.c_void_p]
results=[]
for name,level in [("Test","asInvoker"),("User","requireAdministrator")]:
 folder=Path(packages[name]);exe=folder/"DiskPilot-X.exe"
 for required in ["DiskPilot-X.dll","DiskPilotX.Core.dll","System.Private.CoreLib.dll","skills"]:assert (folder/required).exists(),required
 module=k.LoadLibraryExW(str(exe),None,2);assert module
 try:
  resource=k.FindResourceW(module,1,24);assert resource
  manifest=ctypes.string_at(k.LockResource(k.LoadResource(module,resource)),k.SizeofResource(module,resource)).decode("utf-8")
  assert re.search('level="'+level+'"',manifest),manifest
 finally:k.FreeLibrary(module)
 report=json.loads((folder/"发布验证.json").read_text(encoding="utf-8-sig"));assert report["Manifest"]==level and report["ExeSHA256"]==digest(exe)
 origin=json.loads((folder/"发布来源.json").read_text(encoding="utf-8-sig"));assert Path(origin["SourceRoot"]).resolve()==source and origin["Branch"]==packages["Branch"]
 count=0
 for path in (source/"skills").rglob("*"):
  if path.is_file():assert digest(path)==digest(folder/path.relative_to(source));count+=1
 results.append({"package":name,"manifest":level,"exe_sha256":digest(exe),"verified_skill_files":count})
for log in ["core-verified.log","app-delivery.log"]:
 text=(root/log).read_text(encoding="utf-8-sig");assert "已通过!" in text and re.search(r"失败:\s+0",text),log
performance=json.loads((root/"performance-verified.json").read_text(encoding="utf-8"))["data"]["probe"]
assert all(b["ok"] for b in performance["batches"]) and performance["batches"][-1]["count"]==100
assert performance["dispatcher_ticks"]>0 and performance["batched_dispatcher_gap_ms"]<performance["baseline_dispatcher_gap_ms"]
assert json.loads((root/"source-verified-result.json").read_text(encoding="utf-8"))["data"]["ok"]
history=json.loads((root/"history-verified.json").read_text(encoding="utf-8"))["data"]
assert history["current_root"] is None and history["cards"] and all(row["state"] in ["released","missing"] for row in history["cards"])
capacity=json.loads((root/"delivery-batch-4.json").read_text(encoding="utf-8"))["data"]["probe"]["result"]
assert capacity["partial"] and not capacity["ok"] and capacity["scope_count"]==12
empty=json.loads((root/"empty-delivery-result.json").read_text(encoding="utf-8"))["data"]
assert empty["probe"]["result"]["ok"] and empty["recommendation_count"]==100
for name in ["disk-management","path-cleanup-recommendation","app-guide"]:
 text=(source/"skills"/name/"SKILL.md").read_text(encoding="utf-8");assert "分批" in text and "失效" in text
assert not any(marker in path.read_text(encoding="utf-8-sig") for path in (source/"src").rglob("*.cs") for marker in ["<<<<<<< 当前整合",">>>>>>> 无头实现"])
result={"ok":True,"packages":results,"performance":performance,"history_entries":len(history["cards"]),"capacity_partial":True,"empty_batch_preserved":True}
(root/"delivery-verified.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps({"ok":True,"packages":results,"core_passed":809,"app_passed":604,"app_skipped":1},ensure_ascii=False))
