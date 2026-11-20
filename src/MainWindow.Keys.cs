using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TeyvatGuide
{
    internal sealed partial class MainWindow
    {
        private readonly Dictionary<string,Button> keyButtons = new Dictionary<string,Button>();
        private string recording, candidate;
        private Key candidateKey;
        private bool inKeyDialog, editingText;

        private void WireKeys()
        {
            GotKeyboardFocus += (s,e) => UpdateTypingKeys();
            LostKeyboardFocus += (s,e) => Dispatcher.BeginInvoke(new Action(UpdateTypingKeys));
            Activated += (s,e) => UpdateTypingKeys();
            Deactivated += (s,e) => Dispatcher.BeginInvoke(new Action(UpdateTypingKeys));
            var rows = UI<StackPanel>("KeyRows");
            foreach (var action in Shortcuts.All)
            {
                string id = action.Id;
                var row = new DockPanel { LastChildFill = true };
                var button = new Button { Name = "Key_" + id, Width = 204, MinHeight = 30, Margin = new Thickness(0,5,0,5),
                    Style = (Style)FindResource("KeyButton"), Content = Gesture.Display(settings.Keys[id]), ToolTip = "点击后直接按下新键位" };
                AutomationProperties.SetName(button, "修改" + action.Label + "快捷键");
                button.Click += (s,e) => BeginRecording(id);
                button.LostKeyboardFocus += (s,e) => { if (recording == id) CancelRecording(); };
                keyButtons[id] = button;
                DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
                row.Children.Add(new TextBlock { Text = action.Label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                rows.Children.Add(new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0,0,0,1), Child = row });
            }
            PreviewKeyDown += (s,e) => {
                if (recording == null) return;
                e.Handled = true;
                if (!e.IsRepeat) RecordDown(ActualKey(e), Keyboard.Modifiers);
            };
            PreviewKeyUp += (s,e) => {
                if (recording == null) return;
                e.Handled = true; RecordUp(ActualKey(e));
            };
            Deactivated += (s,e) => { if (recording != null && !closing) CancelRecording(); };
            Click("DefaultKeys", () => {
                if (recording != null) CancelRecording();
                var defaults = Shortcuts.Defaults();
                string error = shortcuts.Replace(defaults, settings.Keys);
                if (error != null) { Status(error); return; }
                settings.Keys = defaults;
                foreach (var action in Shortcuts.All) ResetKeyButton(action.Id);
                QueueSave(); Status("已恢复默认快捷键。");
            });
            var seek = UI<TextBox>("SeekBox");
            seek.KeyDown += (s,e) => { if (e.Key == Key.Enter) { e.Handled = true; ApplySeek(); } };
            seek.LostKeyboardFocus += (s,e) => ApplySeek();
        }
        private static Key ActualKey(KeyEventArgs e)
        {
            if (e.Key == Key.System) return e.SystemKey;
            return e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        }
        private void UpdateTypingKeys()
        {
            if (shortcuts == null || closing || recording != null) return;
            bool typing = inKeyDialog || (IsActive && Keyboard.FocusedElement is TextBoxBase);
            if (typing == editingText) return;
            editingText = typing;
            if (typing) shortcuts.Suspend();
            else {
                string conflict = shortcuts.Register(settings.Keys);
                if (conflict != null) Status(conflict);
            }
        }
        private bool? ShowInputDialog(Func<bool?> show)
        {
            inKeyDialog = true; UpdateTypingKeys();
            try { return show(); }
            finally { inKeyDialog = false; UpdateTypingKeys(); }
        }
        private void ApplySeek()
        {
            int seconds;
            var input = UI<TextBox>("SeekBox");
            if (!int.TryParse(input.Text, out seconds) || seconds < 1 || seconds > 120)
            {
                input.Text = settings.SeekSeconds.ToString();
                Status("跳转步长须为 1 到 120 秒，已恢复原值。");
                return;
            }
            input.Text = seconds.ToString();
            if (seconds == settings.SeekSeconds) return;
            settings.SeekSeconds = seconds; UpdateSeekLabels(); QueueSave(); Status("已更新快进和快退的步长。");
        }
        private void BeginRecording(string id)
        {
            if (shortcuts == null) return;
            if (recording == id) { CancelRecording(); return; }
            if (recording != null) CancelRecording();
            var button = keyButtons[id];
            button.Focus();
            shortcuts.Suspend(); recording = id; candidate = null;
            button.Background = (Brush)FindResource("RecordBackground");
            button.BorderBrush = (Brush)FindResource("Accent");
            button.Content = "请按下新键位…"; Status("直接按一个键，松开保存。按 Esc 取消。");
        }
        private void ResetKeyButton(string id)
        {
            var button = keyButtons[id];
            button.ClearValue(Control.BackgroundProperty); button.ClearValue(Control.BorderBrushProperty);
            button.Content = Gesture.Display(settings.Keys[id]);
        }
        private void RecordDown(Key key, ModifierKeys modifiers)
        {
            if (recording == null) return;
            if (key == Key.Escape) { CancelRecording(); return; }
            if (Gesture.IsModifier(key)) return;
            candidate = Gesture.FromKeys(key, modifiers); candidateKey = key;
            if (candidate == null) { Status("请选择其他键位。可直接按单键，F12 由系统保留。"); return; }
            keyButtons[recording].Content = Gesture.Display(candidate);
        }
        private void RecordUp(Key key)
        {
            if (recording == null || candidate == null || key != candidateKey) return;
            string id = recording, value = candidate; recording = candidate = null;
            var next = new Dictionary<string,string>(settings.Keys); next[id] = value;
            string message;
            if (shortcuts.ApplyRecorded(id, next, settings.Keys, out message)) { settings.Keys = next; QueueSave(); }
            ResetKeyButton(id); Status(message);
        }
        private void CancelRecording()
        {
            if (recording == null) return;
            string id = recording; recording = candidate = null; ResetKeyButton(id);
            if (!closing) Status(shortcuts.Register(settings.Keys) ?? "已取消录制。");
        }
    }
}
