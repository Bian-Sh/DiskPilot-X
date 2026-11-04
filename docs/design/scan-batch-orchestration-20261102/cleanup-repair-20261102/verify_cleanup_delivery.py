from pathlib import Path
import json,hashlib,ctypes,re
root=Path(__file__).resolve().parent;source=root.parents[3]
def read(name):return json.loads((root/name).read_text(encoding="utf-8-sig"))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()
packages=read("packages.json")
k=ctypes.WinDLL("kernel32",use_last_error=True)
k.LoadLibraryExW.argtypes=[ctypes.c_wchar_p,ctypes.c_void_p,ctypes.c_uint];k.LoadLibraryExW.restype=ctypes.c_void_p
k.FindResourceW.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p];k.FindResourceW.restype=ctypes.c_void_p
k.LoadResource.argtypes=[ctypes.c_void_p,ctypes.c_void_p];k.LoadResource.restype=ctypes.c_void_p
k.SizeofResource.argtypes=[ctypes.c_void_p,ctypes.c_void_p];k.SizeofResource.restype=ctypes.c_uint
k.LockResource.argtypes=[ctypes.c_void_p];k.LockResource.restype=ctypes.c_void_p;k.FreeLibrary.argtypes=[ctypes.c_void_p]
reports=[]
for label,level in [("Test","asInvoker"),("User","requireAdministrator")]:
 folder=Path(packages[label]);exe=folder/"DiskPilot-X.exe"
 for name in ["DiskPilot-X.dll","DiskPilotX.Core.dll","System.Private.CoreLib.dll","skills"]:assert (folder/name).exists(),name
 module=k.LoadLibraryExW(str(exe),None,2);assert module
 try:
  res=k.FindResourceW(module,1,24);assert res
  manifest=ctypes.string_at(k.LockResource(k.LoadResource(module,res)),k.SizeofResource(module,res)).decode("utf-8");assert f'level="{level}"' in manifest
 finally:k.FreeLibrary(module)
 report=json.loads((folder/"发布验证.json").read_text(encoding="utf-8-sig"));assert report["Manifest"]==level and report["ExeSHA256"]==digest(exe)
 origin=json.loads((folder/"发布来源.json").read_text(encoding="utf-8-sig"));assert Path(origin["SourceRoot"]).resolve()==source and origin["Branch"]==packages["Branch"]
 for path in (source/"skills").rglob("*"):
  if path.is_file():assert digest(path)==digest(folder/path.relative_to(source))
 reports.append({"package":label,"manifest":level,"exe_sha256":digest(exe)})
kept="本任务未选中的保留资料，不得删除。"
for name in ["manual-final.json","assistant-final.json"]:
 data=read(name)["data"];assert not data["running"] and data["existing_targets"]==0 and data["recommendation_count"]==0
 assert data["kept_file"] and data["kept_content"]==kept
 path=Path(data["probe"]["root"]).resolve();assert path.is_relative_to((root/"runtime/cleanup-fixtures").resolve())
 assert not list(path.glob("cache-*")) and (path/"必须保留.txt").read_text(encoding="utf-8-sig")==kept
assistant=read("assistant-final.json")["data"]["probe"]["result"]
assert assistant["ok"] and len(assistant["results"])==181 and all(row["status"]=="Success" for row in assistant["results"]) and assistant["freed_bytes"]>0
for name in ["changed-final.json","cancel-result.json"]:
 data=read(name)["data"];assert not data["running"] and data["existing_targets"]==181 and data["recommendation_count"]==181
 assert not data["probe"]["result"]["ok"] and data["kept_file"] and data["kept_content"]==kept
 path=Path(data["probe"]["root"]).resolve();assert path.is_relative_to((root/"runtime/cleanup-fixtures").resolve())
 assert len(list(path.glob("cache-*/payload.bin")))==181
assert read("cancel-result.json")["data"]["probe"]["result"]["cancelled"]
assert read("test-token.json")["is_elevated"] is False
probe=read("source-result.json")["data"];assert probe["ok"] and probe["validated"] and probe["count"]==2 and any(row.get("Line") for row in probe["diagnostics"])
assert Path(probe["base_directory"]).resolve()==Path(packages["Test"]).resolve()
for name in ["core-tests.log","app-tests.log"]:
 text=(root/name).read_text(encoding="utf-8-sig");assert "已通过!" in text and re.search(r"失败:\s+0",text),name
assert "ids.Count is 0 or > 100" not in (source/"src/DiskPilotX.Core/Services/Cleanup/CleanupPlan.cs").read_text(encoding="utf-8")
result={"ok":True,"manual_deleted":181,"assistant_deleted":181,"changed_kept":181,"cancelled_kept":181,"unselected_preserved":True,"packages":reports}
(root/"delivery-verified.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8");print(json.dumps(result,ensure_ascii=False))
