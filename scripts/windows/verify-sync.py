"""正式同步验收：使用独立本地 Git 仓库，验证成功、无变化、保护路径和冲突。"""
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

script = Path(__file__).with_name("sync.py").resolve()
results = []

def git(folder, *args):
    return subprocess.check_output(["git", "-C", str(folder), *args], text=True, encoding="utf-8", stderr=subprocess.DEVNULL).strip()

def commit(folder, path, text):
    file = folder / path
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_text(text, encoding="utf-8")
    git(folder, "add", path)
    git(folder, "commit", "-m", "正式验收场景")
    return git(folder, "rev-parse", "HEAD")

def scenario(name, change=None, conflict=False, force=False):
    with tempfile.TemporaryDirectory(prefix="Compositor同步门-") as temporary:
        root = Path(temporary)
        upstream, product, origin = (root / name for name in ("upstream", "product", "origin.git"))
        upstream.mkdir()
        git(upstream, "init", "-b", "main")
        git(upstream, "config", "user.name", "Acceptance")
        git(upstream, "config", "user.email", "acceptance@example.invalid")
        baseline = commit(upstream, "README.md", "原始基线\n")
        subprocess.run(["git", "clone", str(upstream), str(product)], check=True, capture_output=True)
        git(product, "config", "user.name", "Acceptance")
        git(product, "config", "user.email", "acceptance@example.invalid")
        commit(product, "windows/upstream-state.json", json.dumps({"commit": baseline}))
        git(product, "remote", "remove", "origin")
        subprocess.run(["git", "init", "--bare", str(origin)], check=True, capture_output=True)
        git(product, "remote", "add", "origin", str(origin))
        if conflict:
            commit(product, "README.md", "产品分支独立修改\n")
        git(product, "push", "origin", "main")
        product_base = git(product, "rev-parse", "HEAD")
        git(product, "config", "url." + str(upstream).replace("\\", "/") + ".insteadOf", "https://github.com/robbietilton/Compositor.git")
        latest = commit(upstream, change, "上游更新\n") if change else baseline
        output = root / "outputs"
        env = dict(os.environ, PYTHONIOENCODING="utf-8", GITHUB_OUTPUT=str(output), GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1", FORCE_RELEASE=str(force).lower())
        run = subprocess.run([sys.executable, str(script)], cwd=product, env=env, capture_output=True, text=True, encoding="utf-8")
        values = dict(line.split("=", 1) for line in output.read_text().splitlines()) if output.exists() else {}
        assert git(origin, "rev-parse", "main") == product_base, "同步覆盖了已发布产品分支"
        if conflict or change and change != "README.md":
            assert run.returncode != 0 and not values, "保护或冲突场景未阻止发布"
            assert not git(origin, "for-each-ref", "--format=%(refname)", "refs/heads/sync"), "失败却推送了候选"
        elif change or force:
            assert run.returncode == 0, run.stderr
            assert values["changed"] == "true" and values["base"] == product_base
            candidate = git(origin, "rev-parse", values["branch"])
            assert candidate == values["candidate"] and values["upstream"] == latest
            state = json.loads(git(origin, "show", candidate + ":windows/upstream-state.json"))
            assert state["commit"] == latest, "候选基线不一致"
        else:
            assert run.returncode == 0 and values["changed"] == "false"
            assert not git(origin, "for-each-ref", "--format=%(refname)", "refs/heads/sync"), "无变化却产生候选"
        results.append({"scene": name, "passed": True})

scenario("上游无变化不发布")
scenario("无上游变化时可主动发布修复", force=True)
scenario("兼容更新生成候选且不覆盖产品", "README.md")
scenario("格式契约变化阻断发布", "Compositor/IO/ProjectStore.swift")
scenario("发布链变化阻断发布", ".github/workflows/verify.yml")
scenario("签名与产品代码变化阻断发布", "windows/update-public-key.pem")
scenario("合并冲突保留旧产品", "README.md", conflict=True)
print(json.dumps({"failed": 0, "gates": results}, ensure_ascii=False, indent=2))
