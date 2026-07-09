using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public sealed class GoogleAuthService
{
    private const string BreviAuthStartUrl = "https://breviai.vercel.app/api/auth/google/start";
    private readonly string _storePath;

    public GoogleAuthService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "QuickNoteApp");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, "google-auth.json");
    }

    public GoogleAuthState GetState()
    {
        var token = TryReadToken();
        if (token == null)
            return GoogleAuthState.SignedOut;

        var isExpired = token.ExpiresAt <= DateTimeOffset.Now.AddMinutes(2);
        return new GoogleAuthState(true, token.Email, token.Name, isExpired);
    }

    public void SignOut()
    {
        if (File.Exists(_storePath))
            File.Delete(_storePath);
    }

    public async Task<GoogleCalendarCreateResult> CreateCalendarEventAsync(
        string title,
        string description,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        var token = TryReadToken();
        if (token == null)
            return GoogleCalendarCreateResult.Fail("Once Google Giris yapmalisin.");

        if (token.ExpiresAt <= DateTimeOffset.Now.AddMinutes(2))
            return GoogleCalendarCreateResult.Fail("Google oturumu suresi dolmus. Google Giris butonuna tekrar bas.");

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://www.googleapis.com/calendar/v3/calendars/primary/events");

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

            const string calendarTimeZone = "Europe/Istanbul";
            var payload = new
            {
                summary = string.IsNullOrWhiteSpace(title) ? "QuickNote notu" : title.Trim(),
                description = description.Trim(),
                start = new
                {
                    dateTime = ToGoogleDateTime(start),
                    timeZone = calendarTimeZone
                },
                end = new
                {
                    dateTime = ToGoogleDateTime(end),
                    timeZone = calendarTimeZone
                }
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return GoogleCalendarCreateResult.Fail("Google oturumu yenilenmeli. Google Giris butonuna tekrar bas.");

            if (!response.IsSuccessStatusCode)
                return GoogleCalendarCreateResult.Fail($"Google Takvim eklenemedi: {(int)response.StatusCode} {body}");

            using var document = JsonDocument.Parse(body);
            var link = document.RootElement.TryGetProperty("htmlLink", out var linkElement)
                ? linkElement.GetString()
                : null;

            return GoogleCalendarCreateResult.Ok(link);
        }
        catch (Exception ex)
        {
            return GoogleCalendarCreateResult.Fail("Google Takvim hatasi: " + ex.Message);
        }
    }

    public async Task<GoogleAuthResult> SignInAsync(CancellationToken cancellationToken = default)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var callbackUrl = $"http://127.0.0.1:{port}/oauth/google/callback/";
        var authUrl = BreviAuthStartUrl + "?redirect_uri=" + Uri.EscapeDataString(callbackUrl);
        Process.Start(new ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync().WaitAsync(timeout.Token);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync().WaitAsync(timeout.Token)))
            {
                // Basit yerel callback icin header'lari okumamiz yeterli.
            }

            var callbackUri = TryBuildRequestUri(requestLine, port);
            if (callbackUri == null || !callbackUri.AbsolutePath.StartsWith("/oauth/google/callback", StringComparison.OrdinalIgnoreCase))
            {
                await WriteBrowserResponseAsync(stream, "QuickNote Google girisi", "Beklenmeyen cevap alindi. Bu pencereyi kapatabilirsin.");
                return GoogleAuthResult.Fail("Google girisi tamamlanamadi.");
            }

            var query = ParseQuery(callbackUri.Query);
            var error = query.GetValueOrDefault("error");
            if (!string.IsNullOrWhiteSpace(error))
            {
                await WriteBrowserResponseAsync(stream, "Google girisi tamamlanamadi", error);
                return GoogleAuthResult.Fail(error);
            }

            var accessToken = query.GetValueOrDefault("access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                await WriteBrowserResponseAsync(stream, "Google girisi tamamlanamadi", "Google cevap verdi ama token gelmedi.");
                return GoogleAuthResult.Fail("Google cevap verdi ama token gelmedi.");
            }

            var expiresIn = ParseInt(query.GetValueOrDefault("expires_in"), 3600);
            var refreshToken = query.GetValueOrDefault("refresh_token");
            var userInfo = await TryReadUserInfoAsync(accessToken, timeout.Token);
            var email = userInfo?.Email;
            var name = userInfo?.Name;

            var token = new GoogleAuthToken(
                accessToken,
                string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken,
                DateTimeOffset.Now.AddSeconds(expiresIn),
                email,
                name);

            SaveToken(token);
            await WriteBrowserResponseAsync(stream, "Google girisi tamam", "QuickNote Google hesabina baglandi. Bu pencereyi kapatabilirsin.");

            return GoogleAuthResult.Ok(email);
        }
        catch (OperationCanceledException)
        {
            return GoogleAuthResult.Fail("Google girisi zaman asimina ugradi.");
        }
        catch (Exception ex)
        {
            return GoogleAuthResult.Fail(ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    private GoogleAuthToken? TryReadToken()
    {
        try
        {
            if (!File.Exists(_storePath))
                return null;

            var store = JsonSerializer.Deserialize<GoogleAuthStore>(File.ReadAllText(_storePath));
            if (store == null || string.IsNullOrWhiteSpace(store.ProtectedPayload))
                return null;

            var protectedBytes = Convert.FromBase64String(store.ProtectedPayload);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<GoogleAuthToken>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return null;
        }
    }

    private void SaveToken(GoogleAuthToken token)
    {
        var json = JsonSerializer.Serialize(token);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        var store = new GoogleAuthStore(Convert.ToBase64String(protectedBytes));
        File.WriteAllText(_storePath, JsonSerializer.Serialize(store), Encoding.UTF8);
    }

    internal sealed record GoogleUserInfo(string? Email, string? Name);

    private static async Task<GoogleUserInfo?> TryReadUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            
            var email = document.RootElement.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
            var name = document.RootElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            
            return new GoogleUserInfo(email, name);
        }
        catch
        {
            return null;
        }
    }

    public static string GetNameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "Obuzhukuk";

        var parts = email.Split('@');
        var username = parts[0];
        if (string.IsNullOrWhiteSpace(username))
            return "Obuzhukuk";

        return char.ToUpper(username[0]) + username[1..];
    }

    private static async Task WriteBrowserResponseAsync(Stream stream, string title, string message)
    {
        var html = $$"""
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <title>{{WebUtility.HtmlEncode(title)}}</title>
              <style>
                body { font-family: Segoe UI, Arial, sans-serif; background:#0d0f12; color:#f1f5f9; padding:32px; }
                .box { max-width:560px; margin:10vh auto; background:#161920; border:1px solid #2a2e3a; border-radius:12px; padding:24px; }
                h1 { font-size:22px; margin:0 0 12px; }
                p { color:#cbd5e1; line-height:1.5; }
              </style>
            </head>
            <body>
              <div class="box">
                <h1>{{WebUtility.HtmlEncode(title)}}</h1>
                <p>{{WebUtility.HtmlEncode(message)}}</p>
              </div>
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);
        var header = "HTTP/1.1 200 OK\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {bytes.Length}\r\n"
            + "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bytes);
    }

    private static Uri? TryBuildRequestUri(string? requestLine, int port)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
            return null;

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
            return null;

        return Uri.TryCreate($"http://127.0.0.1:{port}{parts[1]}", UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return result;

        foreach (var item in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = value;
        }

        return result;
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, out var result) ? result : fallback;
    }

    private static string ToGoogleDateTime(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local)
            : value.ToLocalTime();

        return new DateTimeOffset(local).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
    }

    public Task<System.Collections.Generic.List<CalendarEvent>?> GetTodayCalendarEventsAsync(CancellationToken cancellationToken = default)
    {
        return GetCalendarEventsAsync(DateTime.Today, cancellationToken);
    }

    public async Task<System.Collections.Generic.List<CalendarEvent>?> GetCalendarEventsAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var token = TryReadToken();
        if (token == null)
            return null;

        if (token.ExpiresAt <= DateTimeOffset.Now.AddMinutes(2))
            return null;

        try
        {
            using var client = new HttpClient();
            var targetDate = date.Date;
            var timeMin = ToGoogleDateTime(targetDate);
            var timeMax = ToGoogleDateTime(targetDate.AddDays(1).AddTicks(-1));

            var url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events" +
                      $"?timeMin={Uri.EscapeDataString(timeMin)}" +
                      $"&timeMax={Uri.EscapeDataString(timeMax)}" +
                      $"&singleEvents=true" +
                      $"&orderBy=startTime";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
                return new System.Collections.Generic.List<CalendarEvent>();

            var list = new System.Collections.Generic.List<CalendarEvent>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                var externalId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var summary = item.TryGetProperty("summary", out var sumEl) ? sumEl.GetString() ?? "" : "";
                var description = item.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
                var location = item.TryGetProperty("location", out var locEl) ? locEl.GetString() ?? "" : "";

                DateTime startVal = targetDate;
                DateTime endVal = targetDate.AddDays(1);
                var isAllDay = false;

                if (item.TryGetProperty("start", out var startEl))
                {
                    if (startEl.TryGetProperty("dateTime", out var dtEl) && DateTime.TryParse(dtEl.GetString(), out var dt))
                        startVal = dt;
                    else if (startEl.TryGetProperty("date", out var dEl) && DateTime.TryParse(dEl.GetString(), out var d))
                    {
                        startVal = d;
                        isAllDay = true;
                    }
                }

                if (item.TryGetProperty("end", out var endEl))
                {
                    if (endEl.TryGetProperty("dateTime", out var dtEl) && DateTime.TryParse(dtEl.GetString(), out var dt))
                        endVal = dt;
                    else if (endEl.TryGetProperty("date", out var dEl) && DateTime.TryParse(dEl.GetString(), out var d))
                        endVal = d;
                }

                list.Add(new CalendarEvent
                {
                    ExternalId = externalId,
                    Summary = summary,
                    Description = description,
                    StartTime = startVal,
                    EndTime = endVal,
                    Location = location,
                    IsAllDay = isAllDay
                });
            }

            return list;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record GoogleAuthState(bool IsSignedIn, string? Email, string? Name, bool IsExpired)
{
    public static GoogleAuthState SignedOut { get; } = new(false, null, null, false);

    public string DisplayText
    {
        get
        {
            if (!IsSignedIn)
                return "Google bagli degil";

            if (IsExpired)
                return "Google oturumu yenilenmeli";

            return string.IsNullOrWhiteSpace(Email)
                ? "Google bagli"
                : "Google: " + Email;
        }
    }
}

public sealed record GoogleAuthResult(bool Success, string? Email, string? Error)
{
    public static GoogleAuthResult Ok(string? email) => new(true, email, null);

    public static GoogleAuthResult Fail(string error) => new(false, null, error);
}

public sealed record GoogleCalendarCreateResult(bool Success, string? Link, string? Error)
{
    public static GoogleCalendarCreateResult Ok(string? link) => new(true, link, null);

    public static GoogleCalendarCreateResult Fail(string error) => new(false, null, error);
}

internal sealed record GoogleAuthStore(string ProtectedPayload);

internal sealed record GoogleAuthToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? Email,
    string? Name);



