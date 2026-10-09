#pragma once
#include "Updater.h"
#include <QString>
namespace compositor::product {
inline const QString stableUrl="https://raw.githubusercontent.com/sun-dove/Compositor/windows-update/native-stable.json";
compositor::production::Options productionOptions();
QByteArray fetchEnvelope(const QString& url=stableUrl);
QString prepareBundle(const QByteArray& envelope,const QString& workingDirectory);
void initializeRoot(const QString& root);
}
