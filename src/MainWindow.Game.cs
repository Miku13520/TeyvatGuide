using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TeyvatGuide
{
    internal sealed partial class MainWindow
    {
        private bool choosingOrLaunchingGame;

        private void WireGameLaunch()
        {
            Click("LaunchGame", () => OpenGame(false));
            Click("GameLocation", () => OpenGame(true));
            UpdateGamePath();
        }

        private void UpdateGamePath()
        {
            var path = UI<TextBlock>("GamePath");
            path.Text = string.IsNullOrEmpty(settings.GameExecutable) ? "自动查找已安装的原神" : settings.GameExecutable;
        }

        private string ChooseGame(string current, bool launching)
        {
            var dialog = new OpenFileDialog {
                Title = launching ? "选择原神游戏程序以启动" : "选择原神游戏程序",
                Filter = "原神游戏程序 (YuanShen.exe, GenshinImpact.exe)|YuanShen.exe;GenshinImpact.exe",
                CheckFileExists = true, AddExtension = false
            };
            if (current != null) {
                dialog.InitialDirectory = Path.GetDirectoryName(current);
                dialog.FileName = Path.GetFileName(current);
            }
            dialog.FileOk += (s,e) => {
                if (GameLauncher.IsGameExecutable(dialog.FileName)) return;
                e.Cancel = true;
                MessageBox.Show(this, "请选择安装目录中的 YuanShen.exe（国服）或 GenshinImpact.exe（国际服）。",
                    "选择游戏程序", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            return ShowInputDialog(() => dialog.ShowDialog(this)) == true ? dialog.FileName : null;
        }

        private async void OpenGame(bool chooseOnly)
        {
            if (choosingOrLaunchingGame || closing) return;
            choosingOrLaunchingGame = true;
            var launch = UI<Button>("LaunchGame");
            var location = UI<Button>("GameLocation");
            launch.IsEnabled = location.IsEnabled = false;
            try
            {
                Status("正在定位原神…");
                string saved = settings.GameExecutable;
                string path = await Task.Run(() => GameLauncher.FindExecutable(saved));
                if (closing) return;
                if (!chooseOnly && path == null)
                {
                    string running = GameLauncher.ActivateRunning();
                    if (running != null) { Status(running); return; }
                    Status("请选择原神安装目录中的游戏程序。");
                }
                if (chooseOnly || path == null)
                {
                    path = ChooseGame(path, !chooseOnly);
                    if (path == null || closing) { Status("已取消选择游戏程序。"); return; }
                }
                settings.GameExecutable = path; UpdateGamePath(); QueueSave();
                if (chooseOnly) { Status("已保存游戏位置。"); return; }
                string existing = GameLauncher.ActivateRunning();
                if (existing != null) { Status(existing); return; }
                GameLauncher.Start(path);
                Status("已请求启动原神。");
            }
            catch (Win32Exception ex) {
                Status(ex.NativeErrorCode == 1223 ? "已取消启动原神。" : "无法启动原神。" + ex.Message);
            }
            catch (Exception ex) { Status("游戏操作失败。" + ex.Message); }
            finally
            {
                choosingOrLaunchingGame = false;
                if (!closing) launch.IsEnabled = location.IsEnabled = true;
            }
        }
    }
}
