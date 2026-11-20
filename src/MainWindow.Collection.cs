using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TeyvatGuide
{
    internal sealed partial class MainWindow
    {
        private bool settingList;
        private void WireCollection()
        {
            Click("ImportLink", () => OpenLink(true)); Click("ImportList", () => OpenLink(true));
            UI<TextBox>("ListLinkBox").KeyDown += (s,e) => { if (e.Key == Key.Enter) { e.Handled = true; OpenLink(true); } };
            var list = UI<ListBox>("Episodes");
            list.SelectionChanged += (s,e) => {
                var collection = settings.Collection;
                if (settingList || collection == null || list.SelectedIndex < 0) return;
                collection.Selected = list.SelectedIndex; UpdateListControls(); QueueSave();
            };
            list.MouseDoubleClick += (s,e) => {
                if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) != null)
                { e.Handled = true; PlaySelected(); }
            };
            list.PreviewKeyDown += (s,e) => { if (e.Key == Key.Enter) { e.Handled = true; PlaySelected(); } };
            Click("PlayEpisode", PlaySelected);
            Click("PreviousEpisode", () => StepEpisode(-1)); Click("NextEpisode", () => StepEpisode(1));
            BindCollection();
        }
        private void BindCollection()
        {
            settingList = true;
            var list = UI<ListBox>("Episodes");
            var c = settings.Collection;
            list.ItemsSource = c == null ? null : c.Episodes;
            if (c != null && c.Episodes.Count > 0) {
                list.SelectedIndex = c.Selected; list.ScrollIntoView(list.SelectedItem);
            }
            settingList = false; UpdateListControls();
        }
        private void UpdateListControls()
        {
            var c = settings.Collection;
            bool has = c != null && c.Episodes.Count > 0;
            UI<TextBlock>("ListTitle").Text = has ? c.Title : "尚未导入合集";
            UI<TextBlock>("ListCount").Text = has ? (c.Selected + 1) + " / " + c.Episodes.Count + " 集" : "";
            UI<TextBlock>("ListHint").Text = has ? "双击分集播放，也可以选中后按回车。" : "粘贴任意一集的分享链接，导入后即可在这里选集。";
            UI<ListBox>("Episodes").Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            UI<FrameworkElement>("ListActions").Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            UI<Border>("DirectoryCard").VerticalAlignment = has ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            UI<Button>("PreviousEpisode").IsEnabled = has && c.Selected > 0;
            UI<Button>("NextEpisode").IsEnabled = has && c.Selected + 1 < c.Episodes.Count;
        }
        private async Task ImportCollectionAsync(Uri address, CancellationToken token)
        {
            Status("正在读取合集目录…");
            var c = await CollectionImporter.ImportAsync(address, token);
            if (token.IsCancellationRequested || closing) return;
            if (c.Episodes.Count == 0) throw new FormatException("这个合集暂时没有可播放的公开分集。");
            settings.Collection = c;
            UI<RadioButton>("TabList").IsChecked = true; BindCollection(); QueueSave();
            var episode = c.Episodes[c.Selected];
            await OpenAsync(BiliLink.Create(episode.VideoId, episode.Part, c.SharedStart), null, token, episode.Title);
            if (!token.IsCancellationRequested) Status("已导入 " + c.Episodes.Count + " 集，双击目录可切换分集。");
        }
        private async Task StepPlayingEpisode(int delta)
        {
            var link = activeGuide;
            var c = settings.Collection;
            if (link == null) { StepEpisode(delta); return; }
            int current = c == null ? -1 : c.Episodes.FindIndex(episode => episode.Key == link.Key);
            if (current < 0) {
                var token = NewRequest();
                Status("正在读取当前攻略的分集目录…");
                try {
                    c = await CollectionImporter.ImportAsync(new Uri(link.Address),token);
                    if (closing || token.IsCancellationRequested || link != activeGuide) return;
                    if (c.Episodes.Count <= 1) { Status("当前攻略只有一集。"); return; }
                    current = c.Selected;
                    settings.Collection = c; BindCollection(); QueueSave();
                }
                catch (Exception ex) { if (!token.IsCancellationRequested && !closing) OpenError(ex); return; }
            }
            StepEpisode(delta,current);
        }
        private void StepEpisode(int delta, int? playingIndex = null)
        {
            var c = settings.Collection;
            if (c == null || c.Episodes.Count == 0) { Status("先导入一个攻略合集即可切集。"); return; }
            int next = (playingIndex ?? c.Selected) + delta;
            if (next < 0 || next >= c.Episodes.Count) {
                Status(delta < 0 ? "已是合集第一集。" : "已是合集最后一集。"); return;
            }
            var list = UI<ListBox>("Episodes"); list.SelectedIndex = next; list.ScrollIntoView(list.SelectedItem);
            PlaySelected();
        }
        private async void PlaySelected()
        {
            var episode = UI<ListBox>("Episodes").SelectedItem as GuideEpisode;
            if (episode == null) return;
            var token = NewRequest();
            try {
                await OpenAsync(BiliLink.Create(episode.VideoId, episode.Part, 0), null, token, episode.Title);
                if (!token.IsCancellationRequested && !closing) await CommandAsync("play", 0);
            }
            catch (Exception ex) { if (!token.IsCancellationRequested) OpenError(ex); }
        }
    }
}
