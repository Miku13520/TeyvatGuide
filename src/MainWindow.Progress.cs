using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TeyvatGuide
{
    internal sealed partial class MainWindow
    {
        private string progressKey;
        private double? resumeTime;
        private int resumeAttempts;
        private DateTime lastResumeAttempt,nextProgressSave;
        private Task CaptureLastPosition()
        {
            if (progressKey==null || resumeTime.HasValue || !player.HasSource) return Task.FromResult(0);
            return Task.WhenAny(ReadLastPosition(player.Generation),Task.Delay(750));
        }
        private async Task ReadLastPosition(int generation)
        {
            try {
                var state=await player.ExecuteAsync("read",null);
                if (state!=null && generation==player.Generation) RememberPlayback(state,true);
            } catch (InvalidOperationException) {
                // The media document was already closed. Keep its most recent sample.
            } catch (COMException) { }
        }

        private async Task<bool> WaitForResume(MediaInfo state)
        {
            if (!resumeTime.HasValue || !state.ready || state.duration<=0) return false;
            double target=Math.Min(resumeTime.Value,state.duration);
            if (!state.seeking && Math.Abs(state.time-target)<.4) {
                resumeTime=null; return false;
            }
            if (DateTime.UtcNow-lastResumeAttempt<TimeSpan.FromMilliseconds(550)) return true;
            if (resumeAttempts>=3) { resumeTime=null; return false; }
            lastResumeAttempt=DateTime.UtcNow;
            resumeAttempts++;
            await player.ExecuteAsync("seek",target);
            return true;
        }
        private void RememberPlayback(MediaInfo state,bool acceptSeek=false)
        {
            if (progressKey==null || resumeTime.HasValue || (!state.ready && !(acceptSeek && state.seeking)) ||
                state.duration<=0 || !(state.time>=0 && state.time<=state.duration+.25)) return;
            double last;
            var time=Math.Round(state.time,2);
            if (settings.PlaybackPositions.TryGetValue(progressKey,out last) && last==time) return;
            settings.PlaybackPositions[progressKey]=time;
            if (DateTime.UtcNow<nextProgressSave) return;
            nextProgressSave=DateTime.UtcNow.AddSeconds(5);
            QueueSave();
        }
    }
}
