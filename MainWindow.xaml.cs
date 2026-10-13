using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LinkDrop;

public partial class MainWindow : Window
{
    public MainViewModel Model { get; }
    private bool canClose;
    private bool isClosing;
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    public MainWindow(string? dataDirectory = null)
    {
        InitializeComponent(); Title = "LinkDrop 1.2.1 — Twoje media, pod ręką"; Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/LinkDrop.ico")); Model = new(dataDirectory); DataContext = Model;
        Model.PropertyChanged += ModelChanged;
        Closing += WindowClosing;
        Navigate("Home"); UpdateModeButtons();
    }
    public void Navigate(string page)
    {
        var pages = new Dictionary<string, (FrameworkElement Panel, Button Nav, string Title, string Eyebrow)>
        {
            ["Home"] = (HomePage, NavHome, "Zapisz coś dobrego.", "TWOJE MEDIA, POD RĘKĄ"),
            ["Queue"] = (QueuePage, NavQueue, "Twoja kolejka.", "WSZYSTKO W SWOIM CZASIE"),
            ["History"] = (HistoryPage, NavHistory, "Dobrze mieć to u siebie.", "HISTORIA POBIERANIA"),
            ["Logs"] = (LogsPage, NavLogs, "Pod maską.", "LOGI I DIAGNOSTYKA"),
            ["Settings"] = (SettingsPage, NavSettings, "Po Twojemu.", "USTAWIENIA APLIKACJI")
        };
        foreach (var (name, item) in pages)
        {
            item.Panel.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
            item.Nav.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(name == page ? "#2B2C35" : "#00101621"));
            item.Nav.Foreground = (Brush)FindResource(name == page ? "Accent" : "Muted");
        }
        PageTitle.Text = pages[page].Title; PageEyebrow.Text = pages[page].Eyebrow; PageScroll.ScrollToTop();
    }
    private async void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Model.Mode)) UpdateModeButtons();
        if (e.PropertyName != nameof(Model.Thumbnail)) return;
        var url = Model.Thumbnail; PreviewImage.Source = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 5_000_000) return;
            using var stream = await response.Content.ReadAsStreamAsync(); using var memory = new MemoryStream();
            byte[] buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer)) > 0) { if (memory.Length + count > 5_000_000) return; memory.Write(buffer, 0, count); }
            memory.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 320; image.StreamSource = memory; image.EndInit(); image.Freeze();
            if (Model.Thumbnail == url) PreviewImage.Source = image;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or TaskCanceledException or System.Runtime.InteropServices.COMException or ArgumentException) { Model.Log("DEBUG", "Miniatura niedostępna: " + ex.Message); }
    }
    private void UpdateModeButtons()
    {
        foreach (var (button, active) in new[] { (AudioButton, Model.Mode == "Audio"), (VideoButton, Model.Mode == "Wideo") })
        { button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#3A304E" : "#202A3C")); button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#D9C4FF" : "#97A5BB")); }
    }
    private void Navigate_Click(object sender, RoutedEventArgs e) => Navigate((string)((Button)sender).Tag);
    private void Paste_Click(object sender, RoutedEventArgs e) { try { if (Clipboard.ContainsText()) Model.Url = Clipboard.GetText().Trim(); } catch (Exception ex) { Model.Notice = "Nie można odczytać schowka: " + ex.Message; } }
    private async void Inspect_Click(object sender, RoutedEventArgs e) => await Model.Inspect();
    private async void Url_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Model.Inspect(); } }
    private void Audio_Click(object sender, RoutedEventArgs e) => Model.Mode = "Audio";
    private void Video_Click(object sender, RoutedEventArgs e) => Model.Mode = "Wideo";
    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Model != null && ((ComboBox)sender).SelectedItem is string value && Model.Format != value) Model.Format = value; }
    private void Quality_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Model != null && ((ComboBox)sender).SelectedItem is string value && Model.Quality != value) Model.Quality = value; }
    private async Task Submit(bool start)
    {
        if (!Model.CanSubmit) return;
        if (!Model.Inspected)
        {
            await Model.Inspect();
            if (!Model.Inspected) return;
            // Let the user review the list before starting a batch of downloads.
            if (Model.IsPlaylist) return;
        }
        if (Model.IsPlaylist) Model.AddPlaylist(start); else Model.Add(start);
    }
    private async void Add_Click(object sender, RoutedEventArgs e) => await Submit(false);
    private async void Download_Click(object sender, RoutedEventArgs e) => await Submit(true);
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Model.SelectPlaylist(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => Model.SelectPlaylist(false);
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog { Title = "Wybierz folder pobierania", InitialDirectory = Directory.Exists(Model.Folder) ? Model.Folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }; if (dialog.ShowDialog(this) == true) { Model.Folder = dialog.FolderName; Model.Persist(); } }
    private void ToggleQueue_Click(object sender, RoutedEventArgs e) => Model.ToggleQueue();
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is Job j) Model.Cancel(j); }
    private void Retry_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is Job j) Model.Retry(j); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is Job j) Model.MoveUp(j); }
    private void ToggleGroup_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is JobGroup group) group.IsExpanded = !group.IsExpanded; }
    private void CancelGroup_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is JobGroup group) Model.CancelGroup(group); }
    private void RetryGroup_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is JobGroup group) Model.RetryGroup(group); }
    private void OpenGroupFolder_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is JobGroup group) Open(group.Folder); }
    private void OpenFile_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is Job j) Open(j.FilePath); }
    private void OpenJobFolder_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not Job j) return;
        if (!File.Exists(j.FilePath)) { Model.Notice = "Plik został przeniesiony lub usunięty."; return; }
        try { var p = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; p.ArgumentList.Add("/select,"); p.ArgumentList.Add(j.FilePath); Process.Start(p); } catch (Exception ex) { Model.Notice = ex.Message; }
    }
    private void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { Model.Notice = "Plik lub folder jeszcze nie istnieje albo został przeniesiony."; return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Model.Notice = ex.Message; }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Open(Model.Folder);
    private void OpenData_Click(object sender, RoutedEventArgs e) => Open(Model.Store.DirectoryPath);
    private void CopyLogs_Click(object sender, RoutedEventArgs e) { try { Clipboard.SetText(Model.LogText); Model.Notice = "Logi skopiowane."; } catch (Exception ex) { Model.Notice = ex.Message; } }
    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "LinkDrop-log.txt", Filter = "Plik tekstowy|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, Model.LogText); Model.Notice = "Zapisano logi."; } catch (Exception ex) { Model.Notice = ex.Message; }
    }
    private void SaveSettings_Click(object sender, RoutedEventArgs e) { Model.Persist(); Model.Notice = "Ustawienia zapisane."; }
    private async void Update_Click(object sender, RoutedEventArgs e) => await Model.Update();
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (canClose) return;
        e.Cancel = true;
        if (isClosing) return;
        if ((Model.IsWorking || Model.IsUpdating) && MessageBox.Show(this, "Trwa pobieranie lub aktualizacja. Przerwać i zamknąć aplikację? Kolejka zostanie zapisana.", "Zamknąć LinkDrop?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        isClosing = true; IsEnabled = false;
        await Model.Stop();
        // A completed Stop task can resume inside the original Closing event.
        // Defer Close until that event has returned to avoid WPF re-entrancy.
        await Dispatcher.InvokeAsync(() => { canClose = true; Close(); }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
