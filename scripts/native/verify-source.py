"""原界面资源与正式生产入口的确定性验收门。"""
import hashlib, json, pathlib, sys
sys.stdout.reconfigure(encoding="utf-8")
root = pathlib.Path(__file__).resolve().parents[2]
expected_hashes = json.loads((root/"product/upstream-state.json").read_text(encoding="utf-8"))["resources"]
# Hashes were measured against the pinned reference Git blobs before importing.
for name, expected in expected_hashes.items():
    actual = (root / "windows-native" / name).read_bytes()
    if name.endswith(".cpp"):
        actual = actual.replace(b"\r\n", b"\n")
    assert hashlib.sha256(actual).hexdigest() == expected, name
for name in ("product/Locale.cpp", "product/ProductMain.cpp", "product/ProductionNetwork.cpp", "product/Gates.cpp"):
    assert (root / name).is_file(), f"尚未实现：{name}"
assert "TEST-ONLY" not in (root / "product/ProductionTrust.h").read_text(encoding="utf-8")
print("PASS：参考字体、图标、样式字节一致；正式入口及生产信任存在")
