using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PrivacyGuard.App;

/// <summary>
/// Minimal WPF integration: starts only the synthetic OBS bridge from a
/// bundled subfolder. A physical camera or unverified model is deliberately
/// NOT exposed through a one-click main-app action.
/// </summary>
public partial class MainWindow
{
    private Process? _liveBridge;
    private bool _liveBridgeStopping;

    private void StartDemoLiveBridge_Click(object sender, RoutedEventArgs e)
    {
        if (_liveBridge is { HasExited: false })
        {
            LiveBridgeStatus.Text = "ブリッジは既に実行中です。";
            return;
        }
        string executable = System.IO.Path.Combine(AppContext.BaseDirectory,
            "live-bridge", "AIPrivacyGuard.LiveBridge.exe");
        if (!File.Exists(executable))
        {
            LiveBridgeStatus.Text = "LiveBridge.exeが見つかりません。GitHubの通常版ZIPをすべて解凍してください。";
            return;
        }

        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("--demo");
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _liveBridgeStopping = false;
        process.OutputDataReceived += (_, output) =>
        {
            if (string.IsNullOrEmpty(output.Data)) return;
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (!ReferenceEquals(_liveBridge, process)) return;
                if (output.Data.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
                {
                    LiveBridgeUrl.Text = output.Data;
                    LiveBridgeStatus.Text = "合成映像デモ起動済み。OBSにURLを設定してね。";
                }
                else if (output.Data.Contains("Retry", StringComparison.OrdinalIgnoreCase)
                         || output.Data.Contains("Disconnected", StringComparison.OrdinalIgnoreCase))
                    LiveBridgeStatus.Text = "接続復旧を試行しています。出力は黒画面です。";
            }));
        };
        process.ErrorDataReceived += (_, output) =>
        {
            if (string.IsNullOrEmpty(output.Data) || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (ReferenceEquals(_liveBridge, process))
                    LiveBridgeStatus.Text = "ブリッジがエラーを報告しました。出力を確認してください。";
            }));
        };
        process.Exited += (_, _) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (!ReferenceEquals(_liveBridge, process)) return;
                LiveBridgeStatus.Text = "ブリッジが終了しました。OBSソースを確認してください。";
                _liveBridge = null;
                process.Dispose();
            }));
        };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("ブリッジ起動に失敗しました。");
            _liveBridge = process;
            LiveBridgeUrl.Text = "起動中...";
            LiveBridgeStatus.Text = "合成映像デモを起動しています。";
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose();
            _liveBridge = null;
            LiveBridgeStatus.Text = "起動できません: " + ex.Message;
        }
    }

    private void StopLiveBridge_Click(object sender, RoutedEventArgs e)
        => StopLiveBridgeProcess();

    private void CopyObsBrowserUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!LiveBridgeUrl.Text.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
        {
            LiveBridgeStatus.Text = "先にデモを起動してURLを取得してください。";
            return;
        }
        try
        {
            Clipboard.SetText(LiveBridgeUrl.Text);
            LiveBridgeStatus.Text = "OBS URLをコピーしました。";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            LiveBridgeStatus.Text = "クリップボードへコピーできませんでした。";
        }
    }

    private void StopLiveBridgeProcess()
    {
        if (_liveBridgeStopping) return;
        _liveBridgeStopping = true;
        var p = _liveBridge;
        _liveBridge = null;
        LiveBridgeStatus.Text = "停止中。OBSソースは黒画面に戻る設計です。";
        LiveBridgeUrl.Text = "";
        if (p is null) { _liveBridgeStopping = false; return; }
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception)
        {
            LiveBridgeStatus.Text = "停止要求が失敗しました。タスクマネージャーで確認してください。";
        }
        finally
        {
            p.Dispose();
            _liveBridgeStopping = false;
        }
    }
}
