using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using PrivacyGuard.Core;

namespace PrivacyGuard.App;

public partial class MainWindow
{
    private ValidationLabStore? _labStore;
    private bool _labBrowserInitAttempted;
    private Uri _labLastRequestedPage = new("https://x.com");
    private sealed record LabDisplayRow(Guid Id, string Display);

    private async void LabTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_labBrowserInitAttempted) return;
        _labBrowserInitAttempted = true;
        try
        {
            string root = System.IO.Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "AIPrivacyGuard");
            string project = System.IO.Path.Combine(root, "validation_lab.json");
            _labStore = ValidationLabStore.Open(project);
            LabProjectPathText.Text = project + " （PC内に保存・公開GitHubへは送信しません）";
            LabRefreshLists();
        }
        catch (Exception ex)
        {
            LabSetStatus("検証データの読み込みに失敗。バックアップを確認してください: " + ex.Message);
            // Do not replace an invalid/corrupted project with an empty one.
        }

        try
        {
            string browserDirectory = System.IO.Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "AIPrivacyGuard", "x-webview2-profile");
            Directory.CreateDirectory(browserDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(null, browserDirectory);
            await LabXBrowser.EnsureCoreWebView2Async(environment);
            LabXBrowser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            LabXBrowser.CoreWebView2.Settings.IsWebMessageEnabled = false;
            LabXBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            LabXBrowser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                    target.Scheme != Uri.UriSchemeHttps)
                {
                    args.Cancel = true;
                    return;
                }
                // The normal X page may link to other sites. Don't scrape or inject JS.
                if (!LabIsXHost(target.Host))
                {
                    args.Cancel = true;
                    LabOpenExternalUrl(target);
                }
            };
            LabXBrowser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) &&
                    target.Scheme == Uri.UriSchemeHttps)
                    LabOpenExternalUrl(target);
            };
            LabXBrowser.CoreWebView2.SourceChanged += (_, _) =>
            {
                var shown = LabXBrowser.Source;
                LabCurrentUrlText.Text = shown is not null && LabIsXHost(shown.Host)
                    ? shown.AbsoluteUri : "Xの外部リンクはブラウザで開きます。";
            };
            LabNavigateX(_labLastRequestedPage);
            LabSetStatus("Xの通常ブラウザ閲覧を開始しました。APIキー・API料金は不要です。");
        }
        catch (Exception ex)
        {
            LabBrowserFallbackText.Visibility = Visibility.Visible;
            LabBrowserFallbackText.Text = "WebView2を初期化できませんでした。外部ブラウザをご利用ください。詳細: " + ex.Message;
            LabSetStatus("ブラウザ内表示は未利用。『外部ブラウザで開く』で検索できます。");
        }
    }

    private static bool LabIsXHost(string host) =>
        host.Equals("x.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("twitter.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".x.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".twitter.com", StringComparison.OrdinalIgnoreCase);

    private void LabNavigateX(Uri page)
    {
        if (page.Scheme != Uri.UriSchemeHttps || !LabIsXHost(page.Host))
            throw new InvalidOperationException("Only X HTTPS pages are opened in the embedded browser.");
        _labLastRequestedPage = page;
        if (LabXBrowser.CoreWebView2 is null)
        {
            LabSetStatus("WebView2が無効です。外部ブラウザで開いてください。");
            return;
        }
        LabXBrowser.CoreWebView2.Navigate(page.AbsoluteUri);
    }

    private static void LabOpenExternalUrl(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps) return;
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }

    private void LabSetStatus(string message)
    {
        if (LabStatusText is not null) LabStatusText.Text = message;
    }

    private void LabSearchX_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string kind = LabXMediaKind.SelectedIndex switch
            {
                1 => "images", 2 => "videos", _ => "all"
            };
            LabNavigateX(XSourceLinks.SearchUrl(LabXSearchText.Text, kind));
        }
        catch (Exception ex) { LabSetStatus("検索できません: " + ex.Message); }
    }

    private void LabUserX_Click(object sender, RoutedEventArgs e)
    {
        try { LabNavigateX(XSourceLinks.UserUrl(LabXSearchText.Text)); }
        catch (Exception ex) { LabSetStatus("ユーザー名を確認してください: " + ex.Message); }
    }

    private void LabBack_Click(object sender, RoutedEventArgs e)
    {
        if (LabXBrowser.CoreWebView2?.CanGoBack == true) LabXBrowser.CoreWebView2.GoBack();
    }

    private void LabForward_Click(object sender, RoutedEventArgs e)
    {
        if (LabXBrowser.CoreWebView2?.CanGoForward == true) LabXBrowser.CoreWebView2.GoForward();
    }

    private void LabRefresh_Click(object sender, RoutedEventArgs e) =>
        LabXBrowser.CoreWebView2?.Reload();

    private void LabExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = LabXBrowser.Source is { } current && LabIsXHost(current.Host)
                ? current : _labLastRequestedPage;
            LabOpenExternalUrl(uri);
        }
        catch (Exception ex) { LabSetStatus("ブラウザを開けません: " + ex.Message); }
    }

    private void LabBookmarkCurrent_Click(object sender, RoutedEventArgs e)
    {
        var current = LabXBrowser.Source;
        if (current is null)
        {
            LabSetStatus("表示中の投稿URLがありません。手動でリンクを貼り付けてください。");
            return;
        }
        LabSaveBookmark(current.AbsoluteUri);
    }

    private void LabBookmarkUrl_Click(object sender, RoutedEventArgs e) =>
        LabSaveBookmark(LabManualPostUrl.Text);

    private void LabSaveBookmark(string candidateUrl)
    {
        if (_labStore is null) { LabSetStatus("検証プロジェクトを開けません。"); return; }
        try
        {
            var link = _labStore.AddPostBookmark(candidateUrl);
            LabManualPostUrl.Text = link.PostUrl;
            LabRefreshLists();
            LabSetStatus("投稿URLを保存しました。Xの写真・動画や投稿本文は保存していません。");
        }
        catch (Exception ex)
        {
            LabSetStatus("投稿のURLを指定してください（https://x.com/ユーザー/status/番号）: " + ex.Message);
        }
    }

    private void LabOpenBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_labStore is null || LabBookmarksList.SelectedItem is not LabDisplayRow row) return;
        var link = _labStore.Bookmarks.FirstOrDefault(b => b.Id == row.Id);
        if (link is null) return;
        try { LabNavigateX(new Uri(link.PostUrl)); }
        catch (Exception ex) { LabSetStatus(ex.Message); }
    }

    private void LabRefreshLists()
    {
        if (_labStore is null) return;
        LabLocalSourcesList.ItemsSource = _labStore.Sources.Select(source =>
            new LabDisplayRow(source.Id,
                $"{source.Kind} / {source.Domain} / {source.Truth} ({source.ReviewState}) : {System.IO.Path.GetFileName(source.Path)}"))
            .ToArray();
        LabBookmarksList.ItemsSource = _labStore.Bookmarks.Select(post =>
            new LabDisplayRow(post.Id, post.PostUrl)).ToArray();
    }

    private async void LabImportLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_labStore is null) { LabSetStatus("プロジェクトの初期化が必要です。"); return; }
        if (LabRightsConfirmed.IsChecked != true)
        {
            LabSetStatus("自分で所有するか、利用許可を確認した素材だけ登録できます。");
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "権利・許可を確認した画像・動画を選ぶ",
            Filter = "画像・動画|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm|すべてのファイル|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string extension = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
            LabMediaKind kind = extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".gif" => LabMediaKind.Image,
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => LabMediaKind.Video,
                _ => throw new InvalidOperationException("検証に対応する画像・動画ファイルを選んでください。")
            };
            var domain = (PrivacyGuard.Core.LabVisualDomain)Math.Clamp(LabVisualDomain.SelectedIndex, 0, 3);
            var source = await _labStore.AddLocalSourceAsync(dialog.FileName, kind, domain, rightsConfirmed: true);
            LabRefreshLists();
            LabSetStatus($"元ファイルを変更せず登録しました: {System.IO.Path.GetFileName(source.Path)}。判定は未確認です。");
            LabRightsConfirmed.IsChecked = false;
        }
        catch (Exception ex) { LabSetStatus("ローカル素材の登録失敗: " + ex.Message); }
    }

    private LabSource? LabSelectedSource() =>
        _labStore is not null && LabLocalSourcesList.SelectedItem is LabDisplayRow row
            ? _labStore.Sources.FirstOrDefault(s => s.Id == row.Id) : null;

    private async void LabOpenLocal_Click(object sender, RoutedEventArgs e)
    {
        var source = LabSelectedSource();
        if (source is null) { LabSetStatus("先に画像・動画を選択してください。"); return; }
        try
        {
            if (_labStore is null || !await _labStore.IsSourceCurrentAsync(source.Id))
            {
                LabSetStatus("ファイルが移動・変更されています。元の判定は信用できません。");
                return;
            }
            if (source.Kind == LabMediaKind.Image)
            {
                await OpenImagePathAsync(source.Path);
                MainTabs.SelectedItem = ImageTab;
                LabSetStatus("画像タブを開きました。確認後、検証ラボに戻って判定してください。");
            }
            else
            {
                Process.Start(new ProcessStartInfo(source.Path) { UseShellExecute = true });
                LabSetStatus("動画を外部プレーヤーで開きました。検証ラボ内のコマ送りは後続開発です。");
            }
        }
        catch (Exception ex) { LabSetStatus("元ファイルを開けません: " + ex.Message); }
    }

    private async void LabMarkNegative_Click(object sender, RoutedEventArgs e)
    {
        var source = LabSelectedSource();
        if (source is null || _labStore is null) { LabSetStatus("画像を選択してください。"); return; }
        if (source.Kind != LabMediaKind.Image)
        {
            LabSetStatus("動画全体を一括して陰性にはできません。フレームごとの確認機能を実装予定です。");
            return;
        }
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(source.Id))
            {
                LabSetStatus("元画像が変更されています。再検証してから登録してください。");
                return;
            }
            var answer = MessageBox.Show(this,
                "画像全体を実際に目視し、対象領域がないことを確認しましたか？\n"
                + "未確認の画像を陰性にするとAI評価が不正確になります。",
                "検証画像の陰性登録", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            _labStore.MarkImageNegative(source.Id, explicitlyInspected: true);
            LabRefreshLists();
            LabSetStatus("目視確認済みの画像を陰性として保存しました。マスク承認とは別の記録です。");
        }
        catch (Exception ex) { LabSetStatus("陰性記録に失敗: " + ex.Message); }
    }

    private async void LabMarkUncertain_Click(object sender, RoutedEventArgs e)
    {
        var source = LabSelectedSource();
        if (source is null || _labStore is null) { LabSetStatus("画像・動画を選んでください。"); return; }
        try
        {
            if (!await _labStore.IsSourceCurrentAsync(source.Id))
            {
                LabSetStatus("元ファイルが変更されたため、再登録が必要です。");
                return;
            }
            _labStore.MarkUncertain(source.Id);
            LabRefreshLists();
            LabSetStatus("判断保留として保存しました。陰性や検証完了としては集計しません。");
        }
        catch (Exception ex) { LabSetStatus("保留記録に失敗: " + ex.Message); }
    }

    private void LabUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_labStore is null) return;
        try
        {
            if (!_labStore.UndoLastSourceEdit())
            {
                LabSetStatus("元に戻せる判定はありません。");
                return;
            }
            LabRefreshLists();
            LabSetStatus("直前の判定を元に戻しました。保存済みです。");
        }
        catch (Exception ex) { LabSetStatus("取り消し失敗: " + ex.Message); }
    }
}
