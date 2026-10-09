const { app, BrowserWindow } = require('electron');
  const path = require('path');
  const updater = require('./updater');

  let mainWindow;

  function createWindow() {
    mainWindow = new BrowserWindow({
      width: 1280,
      height: 800,
      webPreferences: {
        preload: path.join(__dirname, 'preload.js'),
        nodeIntegration: false,
        contextIsolation: true
      }
    });

    mainWindow.loadURL('http://localhost:3000');
  }

  app.whenReady().then(() => {
    createWindow();

    // 自动检查更新
    updater.checkForUpdates();
  });

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
      app.quit();
    }
  });

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
