#include "UpdateDialog.h"
#include "ProductionNetwork.h"
#include <QApplication>
#include <QDialog>
#include <QLabel>
#include <QPushButton>
#include <QVBoxLayout>
#include <QFutureWatcher>
#include <QtConcurrent>
#include <QTemporaryDir>
#include <QPointer>
#include <QProcess>
#include <QTimer>
#include <QDir>
#include <QFile>
#include <QCloseEvent>
namespace compositor::product {
namespace {
struct Result {QString error;QByteArray envelope;bool available{};QString version;};
QString installationRoot(){QDir d(QApplication::applicationDirPath());if(!d.cdUp()||!d.cdUp()||!QFile::exists(d.filePath("install.json")))throw std::runtime_error("请运行安装版以启用自动更新。");return d.absolutePath();}
class Panel final:public QDialog {
    QLabel* status_;QPushButton* action_;QByteArray envelope_;QString root_;bool busy_{},ready_{};bool automatic_{};
public:
    Panel(QWidget* parent,bool automatic):QDialog(parent),automatic_(automatic){
        setAttribute(Qt::WA_DeleteOnClose);setWindowTitle("软件更新");setMinimumWidth(410);auto* layout=new QVBoxLayout(this);
        status_=new QLabel("正在检查 GitHub 更新…",this);status_->setWordWrap(true);layout->addWidget(status_);
        action_=new QPushButton("正在检查…",this);action_->setEnabled(false);layout->addWidget(action_);
        auto* closeButton=new QPushButton("稍后",this);layout->addWidget(closeButton);connect(closeButton,&QPushButton::clicked,this,&QDialog::close);
        connect(action_,&QPushButton::clicked,this,[this]{if(ready_)restart();else if(envelope_.isEmpty())check();else install();});
        check();
    }
    void check(){
        busy_=true;action_->setEnabled(false);auto* watcher=new QFutureWatcher<Result>(this);
        connect(watcher,&QFutureWatcher<Result>::finished,this,[this,watcher]{auto r=watcher->result();watcher->deleteLater();busy_=false;
            if(!r.error.isEmpty()){status_->setText("更新检查失败。"+r.error);action_->setText("重试");action_->setEnabled(true);if(automatic_)deleteLater();return;}
            envelope_=r.envelope;if(r.available){status_->setText("发现新版本 "+r.version+"，可下载并验证后安装。编辑项目将在重启前提示保存。");action_->setText("下载并安装");action_->setEnabled(true);show();}
            else {status_->setText("已是最新版本 "+QApplication::applicationVersion());action_->setText("检查完成");if(automatic_)deleteLater();}
        });
        watcher->setFuture(QtConcurrent::run([]{Result r;try{auto root=installationRoot();production::Updater updater(std::filesystem::path(root.toStdWString()));auto state=updater.state();r.envelope=fetchEnvelope();auto manifest=production::verifyManifest(r.envelope,productionOptions());r.version=manifest.version.text();if(manifest.version<state.current)throw std::runtime_error("更新服务返回了旧版本，已拒绝降级。");r.available=manifest.version>state.current;}catch(const std::exception&e){r.error=QString::fromUtf8(e.what());}return r;}));
    }
    void install(){
        automatic_=false;busy_=true;action_->setEnabled(false);status_->setText("正在下载、核对签名并检查新版本，请稍候…");
        auto* watcher=new QFutureWatcher<Result>(this);const auto envelope=envelope_;
        connect(watcher,&QFutureWatcher<Result>::finished,this,[this,watcher]{auto r=watcher->result();watcher->deleteLater();busy_=false;
            if(!r.error.isEmpty()){status_->setText("更新未完成，当前编辑器可继续使用。"+r.error);action_->setText("重试安装");action_->setEnabled(true);return;}
            ready_=true;status_->setText("新版本 "+r.version+" 已通过完整校验。点击重启，先保存当前项目。");action_->setText("保存并重启");action_->setEnabled(true);
        });
        watcher->setFuture(QtConcurrent::run([envelope]{Result r;try{const auto root=installationRoot();QTemporaryDir dir(root+"/state/download-XXXXXX");if(!dir.isValid())throw std::runtime_error("应用目录不可写。");const auto feed=prepareBundle(envelope,dir.path());production::Updater updater(std::filesystem::path(root.toStdWString()));auto installed=updater.install(feed,productionOptions());r.version=installed.version;}catch(const std::exception&e){r.error=QString::fromUtf8(e.what());}return r;}));
    }
    void restart(){
        const bool quit=qApp->quitOnLastWindowClosed();qApp->setQuitOnLastWindowClosed(false);
        auto* editor=parentWidget();if(editor&&!editor->close()){qApp->setQuitOnLastWindowClosed(quit);return;}
        try{production::launchCurrent(std::filesystem::path(installationRoot().toStdWString()),{});close();qApp->quit();}
        catch(const std::exception&e){if(editor)editor->show();status_->setText("启动失败："+QString::fromUtf8(e.what()));qApp->setQuitOnLastWindowClosed(quit);}
    }
    void closeEvent(QCloseEvent* event)override {
        if(busy_){hide();event->ignore();return;}QDialog::closeEvent(event);
    }
};
QPointer<Panel> panel;
}
void openProductionUpdate(QWidget* parent,bool automatic){if(panel){if(!automatic){panel->show();panel->raise();}return;}panel=new Panel(parent,automatic);if(!automatic)panel->show();}
}
