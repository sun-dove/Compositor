"""原界面资源与正式生产入口的确定性验收门。"""
import hashlib, pathlib, sys
sys.stdout.reconfigure(encoding="utf-8")
root = pathlib.Path(__file__).resolve().parents[2]
expected_hashes = {
 "assets/fonts/InterVariable.ttf":"4989b125924991b90d05b2d16e0e388c48f7d5bb8b30539bbf9c755278d0ccaf",
 "src/ui/EditorIcons.cpp":"9bcd512ea2cb211748942685c286552cfc24885ce520650c501c794a412e4bb8",
 "src/ui/VisualStyle.cpp":"30d1c4b8b1d25058f6d73ba00e29e124d95141bdcec891f572e5b0c0feb7372e"
}
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
