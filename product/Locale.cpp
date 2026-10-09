#include "DisplayLocale.h"
#include <QApplication>
#include <QProxyStyle>
#include <QStyleOption>
#include <QStyledItemDelegate>
#include <QLabel>
#include <QComboBox>
#include <QToolTip>
#include <QHelpEvent>
#include <QHash>
#include <QJsonDocument>
#include <QJsonObject>
#include <QFile>
#include <QFont>
namespace compositor::product {
QString displayText(const QString& source) {
    if(source.contains('\t'))return displayText(source.section('\t',0,0))+"\t"+source.section('\t',1);
    static const auto dictionary=[] {
        QFile file(":/product/zh-CN.json");file.open(QIODevice::ReadOnly);
        return QJsonDocument::fromJson(file.readAll()).object();
    }();
    if(dictionary.contains(source))return dictionary.value(source).toString();
    QString plain=source;plain.remove('&');
    if(dictionary.contains(plain))return dictionary.value(plain).toString();
    for(const QString prefix:{QStringLiteral("Undo "),QStringLiteral("Redo ")})
        if(source.startsWith(prefix))return displayText(prefix.trimmed())+" "+displayText(source.mid(prefix.size()));
    return source;
}
// Rendering-only adaptation: model text, command names, combo selections and
// checkbox text retain their original values for the existing editor logic.
class ChineseStyle final:public QProxyStyle {
public:
    explicit ChineseStyle(QStyle* original):QProxyStyle(original){}
    void drawItemText(QPainter* p,const QRect& r,int flags,const QPalette& palette,bool enabled,const QString& text,QPalette::ColorRole role=QPalette::NoRole) const override {
        QProxyStyle::drawItemText(p,r,flags,palette,enabled,displayText(text),role);
    }
    void drawControl(ControlElement element,const QStyleOption* option,QPainter* painter,const QWidget* widget=nullptr) const override {
        if(auto* o=qstyleoption_cast<const QStyleOptionMenuItem*>(option)){auto copy=*o;copy.text=displayText(copy.text);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        if(auto* o=qstyleoption_cast<const QStyleOptionButton*>(option)){auto copy=*o;copy.text=displayText(copy.text);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        if(auto* o=qstyleoption_cast<const QStyleOptionComboBox*>(option)){auto copy=*o;copy.currentText=displayText(copy.currentText);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        if(auto* o=qstyleoption_cast<const QStyleOptionTab*>(option)){auto copy=*o;copy.text=displayText(copy.text);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        if(auto* o=qstyleoption_cast<const QStyleOptionViewItem*>(option)){auto copy=*o;copy.text=displayText(copy.text);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        if(auto* o=qstyleoption_cast<const QStyleOptionToolButton*>(option)){auto copy=*o;copy.text=displayText(copy.text);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        if(auto* o=qstyleoption_cast<const QStyleOptionDockWidget*>(option)){auto copy=*o;copy.title=displayText(copy.title);QProxyStyle::drawControl(element,&copy,painter,widget);return;}
        QProxyStyle::drawControl(element,option,painter,widget);
    }
};
class ChineseDelegate final:public QStyledItemDelegate {
public:using QStyledItemDelegate::QStyledItemDelegate;
    void initStyleOption(QStyleOptionViewItem* option,const QModelIndex& index)const override {QStyledItemDelegate::initStyleOption(option,index);option->text=compositor::product::displayText(option->text);}
};
class DisplayEvents final:public QObject {
public:using QObject::QObject;
    bool eventFilter(QObject* object,QEvent* event)override {
        auto* w=qobject_cast<QWidget*>(object);if(!w)return false;
        if(event->type()==QEvent::Polish){
            if(auto* combo=qobject_cast<QComboBox*>(w))combo->setItemDelegate(new ChineseDelegate(combo));
        }
        if(event->type()==QEvent::Paint){
            if(auto* label=qobject_cast<QLabel*>(w)){const auto text=displayText(label->text());if(text!=label->text())label->setText(text);}
        }
        if(event->type()==QEvent::Show&&w->isWindow()) {auto text=displayText(w->windowTitle());if(text!=w->windowTitle())w->setWindowTitle(text);}
        if(event->type()==QEvent::ToolTip&&!w->toolTip().isEmpty()) {
            auto* help=static_cast<QHelpEvent*>(event);QToolTip::showText(help->globalPos(),displayText(w->toolTip()),w);return true;
        }
        return false;
    }
};
void installChineseDisplay(QApplication& app) {
    app.setStyle(new ChineseStyle(app.style()));
    // Inter remains the first family; CJK glyphs use Windows fallback.
    auto font=app.font();auto families=font.families();families.append("Microsoft YaHei UI");font.setFamilies(families);app.setFont(font);
    app.installEventFilter(new DisplayEvents(&app));
}
}
