using System;
using System.IO;
using UnityEngine;

namespace RuneSkin.Theme
{
    // [Diagnostics] WatchTheme (perf audit B4): the override theme (BepInEx\config\RuneSkin\theme.json) reloads by itself
    // once a write settled (FileSystemWatcher events arrive on a pool thread and only set a flag; the reload runs here on
    // the main thread, Debounce seconds after the last event: editors write a file in several steps).
    public sealed class ThemeWatcher : MonoBehaviour
    {
        public const float Debounce = 0.5f;
        private FileSystemWatcher _watcher;
        private volatile bool _changed;
        private float _due = -1f;

        public static void Attach(GameObject host)
        {
            var path = ThemeTokens.OverridePath;
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                Directory.CreateDirectory(dir);
                var w = host.AddComponent<ThemeWatcher>();
                w._watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                w._watcher.Changed += w.OnEvent;
                w._watcher.Created += w.OnEvent;
                w._watcher.Deleted += w.OnEvent;
                w._watcher.Renamed += w.OnEvent;
                w._watcher.EnableRaisingEvents = true;
                Plugin.Log.LogInfo($"[RuneSkin] watching {path} (theme reloads {Debounce:0.0} s after a write)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] theme watcher off ({e.Message}); runeskin_reload still reloads");
            }
        }

        private void OnEvent(object sender, FileSystemEventArgs e) => _changed = true;

        private void Update()
        {
            if (_changed)
            {
                _changed = false;
                _due = Time.realtimeSinceStartup + Debounce;
            }
            if (_due < 0f || Time.realtimeSinceStartup < _due) return;
            _due = -1f;
            try
            {
                Plugin.ReloadTheme();
                Plugin.Log.LogInfo($"[RuneSkin] theme reloaded after a change: '{ThemeTokens.ThemeName}' rev {ThemeTokens.Revision} from {ThemeTokens.Source}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RuneSkin] theme reload failed: {ex}");
            }
        }

        private void OnDestroy()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
