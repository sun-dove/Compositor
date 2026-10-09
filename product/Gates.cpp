#include "Gates.h"
#include "DisplayLocale.h"
#include "ProductionNetwork.h"
#include "ui/MainWindow.h"
#include <QApplication>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QFile>
#include <QDir>
#include <QComboBox>
#include <QCheckBox>
#include <QLabel>
#include <QTest>
#include <QMessageBox>
namespace compositor::product {
void runGates(MainWindow& window,const QString& dir){
    QJsonArray checks;auto require=[&](bool good,const char* label){if(!good)throw std::runtime_error(label);checks.append(QString::fromUtf8(label));};
    require(qApp->font().families().first().contains("Inter"),"原 Inter 首选字体保留");
    require(displayText("&File")=="文件"&&displayText("Save Project")=="保存项目","中文菜单和保存显示");
    require(displayText("Free")=="自由"&&displayText("Normal")=="正常","枚举仅显示翻译");
    auto* combo=window.findChild<QComboBox*>("cropRatioChoice");if(combo){require(combo->findText("Free")>=0&&combo->findText("自由")<0,"裁剪内部枚举保留英文契约");}
    QComboBox probe;probe.addItem("Normal");probe.addItem("Multiply");probe.show();QTest::qWait(20);require(probe.currentText()=="Normal"&&probe.itemText(1)=="Multiply","显示翻译不改混合模型值");probe.close();
    QCheckBox checkbox("Sample All Layers");checkbox.show();QTest::qWait(20);require(checkbox.text()=="Sample All Layers","复选框文字契约不变");checkbox.close();
    QLabel label("Save changes before closing this project?");label.show();QTest::qWait(20);require(label.text()=="关闭项目之前保存更改？","中文保存提示实际绘制");label.grab().save(dir+"/save-prompt.png");label.close();
    bool rejected=false;try{production::verifyManifest("{\"keyId\":\"compositor-local-update-test-rsa3072-v1\",\"payload\":\"e30=\",\"signature\":\"AA==\"}",productionOptions());}catch(...){rejected=true;}require(rejected,"公开开发密钥不能进入生产更新");
    rejected=false;try{production::verifyManifest("{}",productionOptions());}catch(...){rejected=true;}require(rejected,"损坏更新元数据拒绝");
    window.addFeasibilityDocument();window.exerciseNativeUi(dir);require(QFile::exists(dir+"/native-ui.json"),"原编辑保存重开完整体验门执行");
    QFile report(dir+"/product-gates.json");require(report.open(QIODevice::WriteOnly),"体验证据可写");report.write(QJsonDocument(QJsonObject{{"status","passed"},{"version",QApplication::applicationVersion()},{"checks",checks}}).toJson());
}
}
