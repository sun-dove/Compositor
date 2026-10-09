#include "ProductionNetwork.h"
#include <QCoreApplication>
#include <QFile>
#include <QTextStream>
#include <QTemporaryDir>
#include <QDir>
#include <Windows.h>
int main(int argc,char** argv){
    QCoreApplication app(argc,argv);auto args=app.arguments();
    auto value=[&](const QString& name){auto i=args.indexOf(name);return i>=0&&i+1<args.size()?args[i+1]:QString{};};
    const auto root=value("--root").isEmpty()?app.applicationDirPath():value("--root");
    try{
        if(args.contains("--initialize"))compositor::product::initializeRoot(root);
        compositor::production::Updater updater(std::filesystem::path(root.toStdWString()));updater.recover();
        if(args.contains("--install")){
            auto feed=value("--feed");QTemporaryDir temporary(root+"/state/download-XXXXXX");
            if(feed.isEmpty()){if(!temporary.isValid())throw std::runtime_error("无法创建更新目录。");feed=compositor::product::prepareBundle(compositor::product::fetchEnvelope(),temporary.path());}
            auto result=updater.install(feed,compositor::product::productionOptions());
            if(args.contains("--quiet"))return 0;
        }
        QStringList forwarded;const auto separator=args.indexOf("--");if(separator>=0)forwarded=args.mid(separator+1);
        return compositor::production::launchCurrent(std::filesystem::path(root.toStdWString()),forwarded,args.contains("--wait"));
    }catch(const std::exception&e){auto message=QString::fromUtf8(e.what());if(!args.contains("--quiet"))MessageBoxW(nullptr,message.toStdWString().c_str(),L"Compositor",MB_OK|MB_ICONERROR);QTextStream(stderr)<<message<<Qt::endl;return 2;}
}
