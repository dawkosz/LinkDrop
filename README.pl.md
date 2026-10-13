# LinkDrop 1.2.1

Aplikacja Windows do pobierania audio, wideo, playlist i albumów obsługiwanych przez yt-dlp. Interfejs jest po polsku, a kolejka i historia pozostają zapisane po zamknięciu programu.

## Pierwsze uruchomienie

1. Pobierz ZIP z **Releases** i rozpakuj wszystkie pliki do jednego folderu.
2. Jeśli nie masz .NET 9, zainstaluj [Desktop Runtime 9 dla Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
3. Otwórz **Start-LinkDrop.cmd**. Przy pierwszym uruchomieniu konfigurator pobierze yt-dlp, FFmpeg/ffprobe i Deno z oficjalnych źródeł, sprawdzając ich sumy SHA-256.
4. Później możesz uruchamiać `LinkDrop.exe` bezpośrednio. Zachowaj folder `tools` obok programu.

Paczka GitHub zawiera aplikację i konfigurator, bez zewnętrznych silników pobierania. Ich instalacja wymaga internetu i około 350 MB miejsca na dysku. Program nie wymaga administratora; instalator środowiska .NET może poprosić o uprawnienia.

## Pobieranie

Wklej link HTTP/HTTPS na Home i wybierz **Sprawdź link**. Ustaw Audio lub Wideo, format, jakość i folder.

- **Do kolejki** dodaje zadanie, pozostawiając kolejkę wstrzymaną.
- **Pobierz teraz** uruchamia zadania po kolei. Nie przerywa obecnego pobrania.
- Możesz zmieniać kolejność, anulować zadania i ponawiać błędy z Historii.
- Wstrzymanie kolejki blokuje rozpoczynanie następnych zadań, a nie aktywny plik.

Audio: MP3, Opus, M4A lub oryginał. Wideo: MP4/MKV z najlepszą dostępną jakością albo limitem rozdzielczości. Wyższy bitrate nie poprawia źródła.

Playlisty pokazują listę pozycji do zatwierdzenia. Zaznacz wszystkie lub wybrane materiały; każdy będzie osobnym zadaniem. Domyślnie trafiają do podfolderu z nazwą albumu. Kolejka i historia grupują utwory z jednego dodania, pokazują postęp i pozwalają ponowić nieudane pozycje. Wyświetlanych jest maksymalnie 500 pozycji.

Nazwy plików zaczynają się od tytułu, bez dodatkowej numeracji utworów. Kolejność pozostaje zapisana wewnętrznie. Obsługiwane serwisy zależą od yt-dlp; logowanie i DRM są poza zakresem aplikacji.

## Ustawienia i logi

Ustawienia, kolejka i historia: `%LOCALAPPDATA%\LinkDrop\state.json`. Kopia zapasowa: `state.json.bak`. Logi są w tym samym lokalnym folderze. Nie są wysyłane do autora aplikacji.

W Ustawieniach można aktualizować yt-dlp przy zatrzymanej kolejce i bez aktywnego pobierania. Logi mogą zawierać tytuły, domeny i lokalne ścieżki; historia zachowuje pełne adresy do ponawiania. Nie udostępniaj ich bez sprawdzenia.

Korzystaj z aplikacji do materiałów, które możesz pobierać. Własny kod jest na licencji MIT; narzędzia zewnętrzne mają odrębne licencje opisane w [THIRD-PARTY.md](THIRD-PARTY.md).

Kod, budowanie i testy: [README.md](README.md).
