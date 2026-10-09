const path = require('path');
  const { VueLoaderPlugin } = require('vue-loader');

  module.exports = {
    entry: './src/index.js',
    output: {
      path: path.resolve(__dirname, 'dist'),
      filename: 'app.js'
    },
    module: {
      rules: [
        {
          test: /\.vue$/,
          loader: 'vue-loader'
        },
        {
          test: /\.js$/,
          exclude: /node_modules/,
          use: 'babel-loader'
        }
      ]
    },
    plugins: [new VueLoaderPlugin()],
    resolve: {
      extensions: ['.js', '.vue', '.json']
    }
  };
