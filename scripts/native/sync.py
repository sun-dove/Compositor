"""双上游候选同步：冲突或受保护路径变更时不触及稳定产品。"""
import hashlib,json,os,subprocess,sys
from pathlib import Path
sys.stdout.reconfigure(encoding="utf-8")
def git(*args):
 return subprocess.check_output(["git",*args],text=True,encoding="utf-8").strip()
def output(**values):
 with open(os.environ["GITHUB_OUTPUT"],"a",encoding="utf-8") as handle:
  for key,value in values.items():handle.write(f"{key}={value}\n")
state_path=Path("product/upstream-state.json")
state=json.loads(state_path.read_text(encoding="utf-8"))
sources=[("mac","https://github.com/robbietilton/Compositor.git","macUpstreamCommit"),("native","https://github.com/IAmTheBlurr/CompositorWindows.git","windowsUpstreamCommit")]
heads={}
for name,url,key in sources:
 git("fetch","--no-tags",url,"main")
 heads[name]=git("rev-parse","FETCH_HEAD")
 # Squashed subtree history does not include the external source object until
 # fetch. Both recorded pins must remain ancestors of the new upstream heads.
 git("merge-base","--is-ancestor",state[key],heads[name])
 changed=git("diff","--name-only",state[key],heads[name]).splitlines()
 protected=("product/","scripts/native/","docs/windows/",".github/")
 blocked=[p for p in changed if p.startswith(protected) or p=="AGENTS.md"]
 if blocked:raise SystemExit("上游涉及受保护发布链或开发规则，停止无人值守发布："+", ".join(blocked))
changed=any(heads[name]!=state[key] for name,_,key in sources)
if not changed and os.environ.get("FORCE_RELEASE")!="true":
 output(changed="false");raise SystemExit(0)
base=git("rev-parse","HEAD");branch="sync/native-"+os.environ["GITHUB_RUN_ID"]+"-"+os.environ["GITHUB_RUN_ATTEMPT"]
git("config","user.name","github-actions[bot]")
git("config","user.email","41898282+github-actions[bot]@users.noreply.github.com")
git("switch","-c",branch)
try:
 if heads["mac"]!=state["macUpstreamCommit"]:git("merge","--no-edit",heads["mac"])
 if heads["native"]!=state["windowsUpstreamCommit"]:
  git("subtree","merge","--prefix","windows-native",heads["native"],"--squash")
  for name in state["resources"]:
   blob=subprocess.check_output(["git","show",heads["native"]+":"+name])
   state["resources"][name]=hashlib.sha256(blob).hexdigest()
except subprocess.CalledProcessError:
 subprocess.run(["git","merge","--abort"],capture_output=True)
 raise SystemExit("上游合并冲突；已发布代码与稳定更新入口保持原版本。")
state["macUpstreamCommit"]=heads["mac"];state["windowsUpstreamCommit"]=heads["native"]
state["windowsMacBaseline"]=json.loads(Path("windows-native/dependencies.lock.json").read_text())["upstream"]
state_path.write_text(json.dumps(state,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
git("add",str(state_path))
if git("diff","--cached","--name-only"):git("commit","-m","记录已同步的两个上游源码基线")
git("push","origin","HEAD:refs/heads/"+branch)
output(changed="true",candidate=git("rev-parse","HEAD"),base=base,branch=branch)
