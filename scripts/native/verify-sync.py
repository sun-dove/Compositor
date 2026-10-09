"""持续交付正式门：七个隔离 Git 场景，执行完成后自动清理样例。"""
import json,os,subprocess,sys,tempfile
from pathlib import Path
sys.stdout.reconfigure(encoding="utf-8")
script=Path(__file__).with_name("sync.py").resolve()
def git(folder,*args):return subprocess.check_output(["git","-C",str(folder),*args],text=True,encoding="utf-8",stderr=subprocess.DEVNULL).strip()
def commit(folder,path,text):
 file=folder/path;file.parent.mkdir(parents=True,exist_ok=True);file.write_text(text,encoding="utf-8")
 git(folder,"add",path);git(folder,"commit","-m","验收场景");return git(folder,"rev-parse","HEAD")
def init(folder):
 folder.mkdir();git(folder,"init","-b","main");git(folder,"config","user.name","Acceptance");git(folder,"config","user.email","acceptance@example.invalid")
results=[]
def scene(label,source=None,path="README.md",conflict=False,force=False):
 with tempfile.TemporaryDirectory(prefix="Compositor-native-sync-") as temporary:
  root=Path(temporary);mac=root/"mac";native=root/"native";product=root/"product";origin=root/"origin.git"
  init(mac);m=commit(mac,"README.md","mac baseline\n")
  init(native);n=commit(native,"dependencies.lock.json",json.dumps({"upstream":m}));commit(native,"README.md","native baseline\n");n=git(native,"rev-parse","HEAD")
  subprocess.run(["git","clone",str(mac),str(product)],check=True,capture_output=True)
  git(product,"config","user.name","Acceptance");git(product,"config","user.email","acceptance@example.invalid")
  git(product,"subtree","add","--prefix","windows-native",str(native),"main","--squash")
  state={"macUpstreamCommit":m,"windowsUpstreamCommit":n,"resources":{}}
  commit(product,"product/upstream-state.json",json.dumps(state))
  if conflict:commit(product,"README.md","product changed\n")
  git(product,"remote","remove","origin");subprocess.run(["git","init","--bare",str(origin)],check=True,capture_output=True);git(product,"remote","add","origin",str(origin));git(product,"push","origin","main")
  base=git(product,"rev-parse","HEAD")
  for repo,url in ((mac,"https://github.com/robbietilton/Compositor.git"),(native,"https://github.com/IAmTheBlurr/CompositorWindows.git")):
   git(product,"config","url."+str(repo).replace("\\","/")+".insteadOf",url)
  if source:commit(mac if source=="mac" else native,path,"upstream changed\n")
  output=root/"output";env=dict(os.environ,GITHUB_OUTPUT=str(output),GITHUB_RUN_ID="123",GITHUB_RUN_ATTEMPT="1",FORCE_RELEASE=str(force).lower(),PYTHONIOENCODING="utf-8")
  result=subprocess.run([sys.executable,str(script)],cwd=product,env=env,capture_output=True,text=True,encoding="utf-8")
  values=dict(line.split("=",1) for line in output.read_text().splitlines()) if output.exists() else {}
  assert git(origin,"rev-parse","main")==base,"已发布分支被同步脚本覆盖"
  blocked=conflict or path.startswith(("product/",".github/")) and source is not None
  if blocked:
   assert result.returncode!=0 and not values and not git(origin,"for-each-ref","--format=%(refname)","refs/heads/sync"),result.stderr
  elif source or force:
   assert result.returncode==0 and values["changed"]=="true",result.stderr
   candidate=git(origin,"rev-parse",values["branch"]);assert candidate==values["candidate"]
   state=json.loads(git(origin,"show",candidate+":product/upstream-state.json"))
   assert state["macUpstreamCommit"]==git(mac,"rev-parse","HEAD") and state["windowsUpstreamCommit"]==git(native,"rev-parse","HEAD")
   if source=="native":assert git(origin,"show",candidate+":windows-native/README.md")=="upstream changed"
  else:assert result.returncode==0 and values["changed"]=="false",result.stderr
  results.append({"场景":label,"通过":True})
scene("无变化不发布")
scene("主动发布产品修复",force=True)
scene("Mac 原码更新同步到候选","mac")
scene("Windows 子树更新保留产品扩展","native")
scene("Mac 发布链变更阻断","mac",".github/workflows/build.yml")
scene("Windows 生产密钥路径冲突阻断","native","product/ProductionTrust.h")
scene("合并冲突保留稳定版本","mac",conflict=True)
print(json.dumps({"failed":0,"gates":results},ensure_ascii=False,indent=2))
