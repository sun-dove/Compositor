"""完整候选资产公开、远端安装验收后，原子推进稳定索引。"""
import base64,json,os,subprocess,urllib.request,urllib.error
from pathlib import Path
def git(*args):return subprocess.check_output(["git",*args],text=True).strip()
def api(method,path,body=None):
 request=urllib.request.Request("https://api.github.com/"+path,data=None if body is None else json.dumps(body).encode(),method=method,headers={"Authorization":"Bearer "+os.environ["GH_TOKEN"],"Accept":"application/vnd.github+json","User-Agent":"CompositorNativeRelease"})
 with urllib.request.urlopen(request,timeout=60) as response:return json.load(response)
repo=os.environ["GITHUB_REPOSITORY"];candidate=os.environ["CANDIDATE_SHA"];base=os.environ["BASE_SHA"];version=os.environ["PRODUCT_VERSION"]
branch=os.environ.get("PRODUCT_BRANCH","main");release=Path(os.environ.get("RELEASE_DIRECTORY","native-artifacts/release"));tag="windows-v"+version
assert (release/"native-feed.json").is_file() and (release/f"Compositor-{version}-Setup.exe").is_file(),"缺少完整候选资产"
remote=api("GET",f"repos/{repo}/releases/tags/{tag}")
sizes={asset["name"]:asset["size"] for asset in remote["assets"]}
for file in release.iterdir():
 if file.is_file():assert sizes.get(file.name)==file.stat().st_size,"远端资产不完整"
git("fetch","origin",branch);current=git("rev-parse","FETCH_HEAD")
assert current in {base,candidate},"产品分支已推进，停止更新索引，等待重建"
git("merge-base","--is-ancestor",current,candidate)
git("push","origin",candidate+":refs/heads/"+branch)
try:api("GET",f"repos/{repo}/git/refs/heads/windows-update")
except urllib.error.HTTPError as e:
 if e.code!=404:raise
 api("POST",f"repos/{repo}/git/refs",{"ref":"refs/heads/windows-update","sha":candidate})
body={"message":"发布已验收原界面 Windows 更新 "+version,"branch":"windows-update","content":base64.b64encode((release/"native-feed.json").read_bytes()).decode()}
try:body["sha"]=api("GET",f"repos/{repo}/contents/native-stable.json?ref=windows-update")["sha"]
except urllib.error.HTTPError as e:
 if e.code!=404:raise
api("PUT",f"repos/{repo}/contents/native-stable.json",body)
subprocess.run(["gh","release","edit",tag,"--repo",repo,"--prerelease=false","--latest"],check=True)
print("稳定源码、签名更新入口及完整资产已发布："+tag)
