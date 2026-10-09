"""先公开完整 Release，再原子推进签名更新入口；重复运行可恢复。"""
import base64
import json
import os
import subprocess
import urllib.error
import urllib.request
from pathlib import Path

def git(*arguments):
    return subprocess.check_output(["git", *arguments], text=True).strip()

def api(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request("https://api.github.com/" + path, data=data, method=method, headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"], "Accept": "application/vnd.github+json", "User-Agent": "CompositorWindowsRelease", "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)

repository = os.environ["GITHUB_REPOSITORY"]
candidate = os.environ["CANDIDATE_SHA"]
base = os.environ["BASE_SHA"]
default_branch = os.environ["PRODUCT_BRANCH"]
version = os.environ["PRODUCT_VERSION"]
tag = "windows-v" + version
release_dir = Path("release")
assert (release_dir / "stable.json").is_file(), "缺少签名清单"
assert any(release_dir.glob("*-Setup.exe")), "缺少初次安装包"
git("fetch", "origin", default_branch)
current = git("rev-parse", "FETCH_HEAD")
if current not in {base, candidate}:
    raise SystemExit("产品分支已由其他提交推进，停止发布并等待重新运行。")
git("merge-base", "--is-ancestor", current, candidate)
git("push", "origin", f"{candidate}:refs/heads/{default_branch}")
found = subprocess.run(["gh", "release", "view", tag, "--repo", repository], capture_output=True)
if found.returncode:
    subprocess.run(["gh", "release", "create", tag, "--repo", repository, "--target", candidate, "--draft", "--title", "Compositor Windows 中文版 " + version, "--notes", "自动同步、Windows 验收与打包已通过。支持基础图层；高级能力仍按兼容场景逐项适配。"], check=True)
for file in sorted(release_dir.iterdir()):
    if file.is_file():
        subprocess.run(["gh", "release", "upload", tag, str(file), "--repo", repository, "--clobber"], check=True)
release = api("GET", f"repos/{repository}/releases/tags/{tag}")
sizes = {asset["name"]: asset["size"] for asset in release["assets"]}
for file in release_dir.iterdir():
    if file.is_file() and sizes.get(file.name) != file.stat().st_size:
        raise SystemExit("远端资产不完整，更新索引未推进。")
subprocess.run(["gh", "release", "edit", tag, "--repo", repository, "--draft=false", "--latest"], check=True)
ref_path = f"repos/{repository}/git/refs/heads/windows-update"
try:
    api("GET", ref_path)
except urllib.error.HTTPError as error:
    if error.code != 404:
        raise
    api("POST", f"repos/{repository}/git/refs", {"ref": "refs/heads/windows-update", "sha": candidate})
body = {"message": "发布 Windows 签名更新入口 " + version, "branch": "windows-update", "content": base64.b64encode((release_dir / "stable.json").read_bytes()).decode()}
try:
    previous = api("GET", f"repos/{repository}/contents/stable.json?ref=windows-update")
    body["sha"] = previous["sha"]
except urllib.error.HTTPError as error:
    if error.code != 404:
        raise
api("PUT", f"repos/{repository}/contents/stable.json", body)
print("正式 Release 与签名更新入口均已发布：" + tag)
