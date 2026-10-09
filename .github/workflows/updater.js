const { autoUpdater } = require('electron-updater');
  const { dialog } = require('electron');

  autoUpdater.setFeedURL({
    provider: 'github',
    repo: 'Compositor',
    owner: 'sun-dove',
    releaseType: 'release'
  });

  autoUpdater.on('update-available', () => {
    dialog.showMessageBox({
      type: 'info',
      title: '更新',
      message: '发现新版本，是否立即更新？',
      buttons: ['是', '否'],
      defaultId: 0
    }).then(result => {
      if (result.response === 0) {
        autoUpdater.quitAndInstall();
      }
    });
  });

  autoUpdater.on('update-downloaded', () => {
    dialog.showMessageBox({
      type: 'info',
      title: '更新',
      message: '新版本已下载，是否立即安装？',
      buttons: ['是', '否'],
      defaultId: 0
    }).then(result => {
      if (result.response === 0) {
        autoUpdater.quitAndInstall();
      }
    });
  });
