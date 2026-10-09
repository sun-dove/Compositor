#pragma once
#include <QString>
class QApplication;
namespace compositor::product {
QString displayText(const QString& source);
void installChineseDisplay(QApplication& app);
}
