"""派生独立生产命名空间；原开发更新接口保持不变。"""
import pathlib, sys
root = pathlib.Path(__file__).resolve().parents[2]
out = pathlib.Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
for name in ("Updater.h", "Updater.cpp"):
    text = (root/"windows-native/src/update"/name).read_text(encoding="utf-8")
    text = text.replace("namespace compositor::update", "namespace compositor::production")
    text = text.replace('"TestTrust.h"', '"ProductionTrust.h"')
    text = text.replace("allowTestKey", "allowProductionKey")
    text = text.replace('"test"', '"stable"')
    text = text.replace('"Development update signing key requires explicit opt-in"', '"Production trust requires explicit opt-in"')
    text = text.replace('"This development updater accepts only the test channel"', '"Production updater accepts only the stable channel"')
    if name.endswith(".cpp"):
        old = 'keys(object,{"schema","product","channel","version","files"})'
        assert text.count(old) == 1, "上游更新协议变化，需要人工适配"
        text = text.replace(old, 'keys(object,{"schema","product","channel","version","files","bundle"})')
    (out/name).write_text(text, encoding="utf-8")
