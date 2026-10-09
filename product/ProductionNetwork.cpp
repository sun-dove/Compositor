#include "ProductionNetwork.h"
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QEventLoop>
#include <QTimer>
#include <QFile>
#include <QDir>
#include <QJsonDocument>
#include <QJsonObject>
#include <QCryptographicHash>
#include <QRegularExpression>
#include <QProcess>
#include <QSaveFile>
#include <QStorageInfo>
#include <QCoreApplication>
#include <cmath>
namespace compositor::product {
namespace {
[[noreturn]]void fail(const QString& text){throw std::runtime_error(text.toUtf8().constData());}
void write(const QString& path,const QByteArray& bytes){QSaveFile file(path);if(!file.open(QIODevice::WriteOnly)||file.write(bytes)!=bytes.size()||!file.commit())fail("无法写入更新文件，检查磁盘空间和目录权限。");}
void download(const QUrl& url,qint64 maximum,const std::function<void(const QByteArray&)>& consume){
    if(url.scheme()!="https"||!url.userInfo().isEmpty())fail("更新地址必须使用 HTTPS。");
    QNetworkAccessManager manager;QNetworkRequest request(url);
    request.setAttribute(QNetworkRequest::RedirectPolicyAttribute,QNetworkRequest::NoLessSafeRedirectPolicy);
    request.setRawHeader("User-Agent","Compositor-sun-dove/1");
    auto* reply=manager.get(request);QEventLoop loop;QTimer timer;timer.setSingleShot(true);qint64 size=0;QString error;
    QObject::connect(&timer,&QTimer::timeout,&loop,[&]{error="更新网络请求超时，请稍后重试。";reply->abort();});
    QObject::connect(reply,&QNetworkReply::readyRead,&loop,[&]{auto bytes=reply->readAll();size+=bytes.size();if(size>maximum){error="下载内容超过签名限制。";reply->abort();return;}try{consume(bytes);}catch(const std::exception&e){error=QString::fromUtf8(e.what());reply->abort();}});
    QObject::connect(reply,&QNetworkReply::finished,&loop,&QEventLoop::quit);timer.start(15*60*1000);loop.exec();
    if(!error.isEmpty())fail(error);
    if(reply->error()!=QNetworkReply::NoError||reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt()!=200)fail("无法连接 GitHub 更新服务："+reply->errorString());
}
}
production::Options productionOptions(){production::Options options;options.allowProductionKey=true;return options;}
QByteArray fetchEnvelope(const QString& url){QByteArray bytes;download(QUrl(url),2*1024*1024,[&](const QByteArray& b){bytes+=b;});production::verifyManifest(bytes,productionOptions());return bytes;}
QString prepareBundle(const QByteArray& envelope,const QString& workingDirectory){
    const auto manifest=production::verifyManifest(envelope,productionOptions());
    const auto payload=QJsonDocument::fromJson(manifest.signedBytes).object();const auto bundle=payload.value("bundle").toObject();
    const auto size=bundle.value("size").toDouble(-1);const auto hash=bundle.value("sha256").toString();
    if(size<1||size>1024.*1024*1024||std::floor(size)!=size||!QRegularExpression("^[0-9a-f]{64}$").match(hash).hasMatch())fail("更新压缩包信息无效。");
    quint64 total=0;for(const auto& file:manifest.files)total+=file.size;
    QStorageInfo disk(workingDirectory);if(disk.bytesAvailable()<qint64(total*2+size+100*1024*1024))fail("磁盘空间不足，请释放空间后重试。");
    QDir dir(workingDirectory);if(!dir.mkdir("feed"))fail("无法创建独立更新暂存目录。");const auto feed=dir.filePath("feed");
    const auto archive=dir.filePath("bundle.zip");QFile file(archive);if(!file.open(QIODevice::WriteOnly|QIODevice::NewOnly))fail("无法创建更新压缩包。");QCryptographicHash digest(QCryptographicHash::Sha256);qint64 received=0;
    const QString asset="Compositor-"+manifest.version.text()+"-payload.zip";
    const auto url=QUrl("https://github.com/sun-dove/Compositor/releases/download/windows-v"+manifest.version.text()+"/"+asset);
    download(url,qint64(size),[&](const QByteArray& bytes){if(file.write(bytes)!=bytes.size())fail("更新下载写入失败。");digest.addData(bytes);received+=bytes.size();});file.close();
    if(received!=qint64(size)||digest.result().toHex()!=hash.toLatin1())fail("更新压缩包校验失败，当前版本保持可用。");
    write(feed+"/feed.json",envelope);
    QProcess extract;extract.start("powershell.exe",{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",QCoreApplication::applicationDirPath()+"/Expand-Verified.ps1","-Archive",archive,"-Feed",feed});
    if(!extract.waitForFinished(180000)||extract.exitCode()!=0)fail("更新压缩包解压失败："+QString::fromUtf8(extract.readAllStandardError()));
    return feed;
}
void initializeRoot(const QString& root){
    QDir dir(root);if(!dir.exists()&&!QDir().mkpath(root))fail("无法创建安装目录。");
    if(QFile::exists(dir.filePath("install.json")))return;
    const auto existing=dir.entryList(QDir::AllEntries|QDir::NoDotAndDotDot);
    // Bootstrap executables are deployed by the installer; user documents are
    // never imported into a version directory or replaced by an update.
    for(const auto& entry:existing)if(entry.endsWith(".comp",Qt::CaseInsensitive)||entry=="versions"||entry=="state")fail("目标目录含有未识别的项目或版本数据，请选择空应用目录。");
    if(!dir.mkpath("versions")||!dir.mkpath("state"))fail("安装目录权限不足。");
    write(dir.filePath("install.json"),"{\"schema\":1,\"product\":\"compositor-windows\"}");
    write(dir.filePath("state/active.json"),"{\"schema\":1,\"product\":\"compositor-windows\",\"channel\":\"stable\",\"current\":\"0.0.0\",\"previous\":\"\",\"pending\":false}");
}
}
