"""在独立候选分支同步上游；不直接修改已发布分支。"""
import json
import os
import subprocess
from pathlib import Path

def git(*arguments):
    return subprocess.check_output(["git", *arguments], text=True, encoding="utf-8").strip()

def output(**values):
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as handle:
        for key, value in values.items():
            handle.write(f"{key}={value}\n")

state_path = Path("windows/upstream-state.json")
state = json.loads(state_path.read_text(encoding="utf-8"))
git("fetch", "--no-tags", "https://github.com/robbietilton/Compositor.git", "main")
upstream = git("rev-parse", "FETCH_HEAD")
git("merge-base", "--is-ancestor", state["commit"], upstream)
changed = git("diff", "--name-only", state["commit"], upstream).splitlines()
protected_prefixes = ("windows/", "scripts/", ".github/", "docs/windows/")
contract_files = {"Compositor/IO/ProjectStore.swift", "docs/project-format.md", "Compositor/Document/LayerTransform.swift", "Compositor/Document/LayerAppearance.swift", "Compositor/Document/LayerGroups.swift"}
blocked = [path for path in changed if path.startswith(protected_prefixes) or path in contract_files or path.endswith((".props", ".targets", ".csproj", ".ps1", ".py")) or path in {"global.json", "NuGet.Config", "AGENTS.md"}]
if blocked:
    raise SystemExit("上游涉及发布链、开发规则或 Windows 兼容契约，已阻止无人值守发布：\n" + "\n".join(blocked))
force = os.environ.get("FORCE_RELEASE") == "true"
if upstream == state["commit"] and not force:
    output(changed="false", upstream=upstream)
    raise SystemExit(0)
base = git("rev-parse", "HEAD")
branch = "sync/windows-" + os.environ["GITHUB_RUN_ID"] + "-" + os.environ["GITHUB_RUN_ATTEMPT"]
git("config", "user.name", "github-actions[bot]")
git("config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com")
git("switch", "-c", branch)
if upstream != state["commit"]:
    try:
        git("merge", "--no-edit", upstream)
    except subprocess.CalledProcessError:
        git("merge", "--abort")
        raise SystemExit("同步发生冲突；产品分支和可用版本未被覆盖。")
    state["commit"] = upstream
    state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    git("add", str(state_path))
    git("commit", "-m", "记录自动同步的上游基线")
git("push", "origin", "HEAD:refs/heads/" + branch)
output(changed="true", candidate=git("rev-parse", "HEAD"), upstream=upstream, base=base, branch=branch)
