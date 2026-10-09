"""原界面资源与正式生产入口的确定性验收门。"""
import hashlib, pathlib, subprocess, sys
sys.stdout.reconfigure(encoding="utf-8")
root = pathlib.Path(__file__).resolve().parents[2]
pin = "dfd0044d39b63ea49112a0e316a9460af29acdea"
for name in ("assets/fonts/InterVariable.ttf", "src/ui/EditorIcons.cpp", "src/ui/VisualStyle.cpp"):
    expected = subprocess.check_output(["git", "show", f"{pin}:{name}"], cwd=root)
    actual = (root / "windows-native" / name).read_bytes()
    if name.endswith(".cpp"):
        actual = actual.replace(b"\r\n", b"\n")
    assert hashlib.sha256(actual).digest() == hashlib.sha256(expected).digest(), name
for name in ("product/Locale.cpp", "product/ProductMain.cpp", "product/ProductionNetwork.cpp", "product/Gates.cpp"):
    assert (root / name).is_file(), f"尚未实现：{name}"
assert "TEST-ONLY" not in (root / "product/ProductionTrust.h").read_text(encoding="utf-8")
print("PASS：参考字体、图标、样式字节一致；正式入口及生产信任存在")
